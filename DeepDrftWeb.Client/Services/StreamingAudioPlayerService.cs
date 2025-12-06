using DeepDrftModels.Entities;
using DeepDrftWeb.Client.Clients;
using System.Buffers;
using Microsoft.Extensions.Logging;

namespace DeepDrftWeb.Client.Services;

public class StreamingAudioPlayerService : AudioPlayerService, IStreamingPlayerService
{
    // Configuration constants  
    private const int DefaultBufferSize = 32 * 1024; // 32KB chunks
    private const int NotificationThrottleMs = 100; // Throttle UI updates to max 10 per second
    
    // Adaptive chunk sizing
    private const int MinBufferSize = 16 * 1024;  // 16KB minimum
    private const int MaxBufferSize = 64 * 1024;  // 64KB maximum
    private int _currentBufferSize = DefaultBufferSize;
    private int _consecutiveSlowReads = 0;
    
    
    // Streaming state properties
    public bool IsStreamingMode { get; private set; } = false;
    public bool CanStartStreaming { get; private set; } = false;
    public bool HeaderParsed { get; private set; } = false;
    public int BufferedChunks { get; private set; } = 0;
    
    private bool _streamingPlaybackStarted = false;
    private CancellationTokenSource? _streamingCancellation;
    private DateTime _lastNotification = DateTime.MinValue;
    private readonly ILogger<StreamingAudioPlayerService> _logger;

    public StreamingAudioPlayerService(
        AudioInteropService audioInterop, 
        TrackMediaClient trackMediaClient,
        ILogger<StreamingAudioPlayerService> logger)
        : base(audioInterop, trackMediaClient)
    {
        _logger = logger;
    }

    public override async Task SelectTrack(TrackEntity track)
    {
        await SelectTrackStreaming(track);
    }

    public async Task SelectTrackStreaming(TrackEntity track)
    {
        await EnsureInitializedAsync();

        // Resume AudioContext immediately on track selection (user interaction) to avoid clicks later
        await _audioInterop.EnsureAudioContextReady(PlayerId);

        // NotifyStateChanged();

        await NotifyTrackSelected();

        await LoadTrackStreaming(track);
        await NotifyStateChanged();
    }

    private async Task LoadTrackStreaming(TrackEntity track)
    {
        // Always reset to clean state before loading new track
        await ResetToIdle();

        // Create new cancellation token for this streaming operation
        _streamingCancellation = new CancellationTokenSource();

        try
        {
            // Set state to indicate loading has started
            ErrorMessage = null;
            LoadProgress = 0;
            IsLoading = true;
            IsStreamingMode = true;

            // Reset adaptive buffer sizing
            _currentBufferSize = DefaultBufferSize;
            _consecutiveSlowReads = 0;

            await NotifyStateChanged();

            var mediaResult = await _trackMediaClient.GetTrackMedia(track.EntryKey);
            if (!mediaResult.Success)
            {
                var technicalError = mediaResult.GetMessage();
                _logger.LogError("Failed to get track media for {TrackId}: {Error}",
                    track.EntryKey, technicalError);
                ErrorMessage = StreamingErrorHandler.GetUserFriendlyMessage(technicalError);
                return;
            }

            if (mediaResult.Value == null)
            {
                const string technicalError = "No audio returned from server";
                _logger.LogError("No audio data returned for track {TrackId}", track.EntryKey);
                ErrorMessage = StreamingErrorHandler.GetUserFriendlyMessage(technicalError);
                return;
            }

            using var audio = mediaResult.Value;

            // Initialize streaming mode with content length
            var streamingResult = await _audioInterop.InitializeStreaming(PlayerId, audio.ContentLength);
            if (!streamingResult.Success)
            {
                var technicalError = $"Failed to initialize streaming: {streamingResult.Error}";
                _logger.LogError("Streaming initialization failed for track {TrackId}: {Error}",
                    track.EntryKey, technicalError);
                ErrorMessage = StreamingErrorHandler.GetUserFriendlyMessage(technicalError);
                return;
            }

            await StreamAudioWithEarlyPlayback(audio, _streamingCancellation.Token);
        }
        catch (OperationCanceledException)
        {
            // Cancellation is expected, reset state
            _logger.LogDebug("Audio streaming cancelled for track {TrackId}", track.EntryKey);
            IsLoaded = false;
            IsStreamingMode = false;
        }
        catch (Exception ex)
        {
            StreamingErrorHandler.LogError(_logger, ex, "LoadTrackStreaming", track.EntryKey);
            ErrorMessage = StreamingErrorHandler.GetUserFriendlyMessage(ex.Message);
            LoadProgress = 0;
            IsLoaded = false;
            IsStreamingMode = false;
        }
        finally
        {
            IsLoading = false;
            await NotifyStateChanged();
        }
    }

    private async Task StreamAudioWithEarlyPlayback(TrackMediaResponse audio, CancellationToken cancellationToken)
    {
        byte[]? buffer = null;
        try
        {
            long totalBytesRead = 0;
            buffer = ArrayPool<byte>.Shared.Rent(MaxBufferSize); // Rent larger buffer to accommodate adaptive sizing
            int currentBytes;
            var readTimer = System.Diagnostics.Stopwatch.StartNew();
            
            do
            {
                readTimer.Restart();
                currentBytes = await audio.Stream.ReadAsync(buffer, 0, _currentBufferSize, cancellationToken);
                readTimer.Stop();
                
                // Adapt buffer size based on read performance
                AdaptBufferSize(currentBytes, readTimer.ElapsedMilliseconds);
                
                if (currentBytes > 0)
                {
                    totalBytesRead += currentBytes;
                    
                    // Use only the actual bytes read, no copying needed
                    var actualBuffer = currentBytes == _currentBufferSize ? buffer : buffer[..currentBytes];
                    
                    // Process chunk for streaming
                    var chunkResult = await _audioInterop.ProcessStreamingChunk(PlayerId, actualBuffer);
                    if (!chunkResult.Success)
                    {
                        var error = $"Failed to process streaming chunk: {chunkResult.Error}";
                        _logger.LogWarning("Chunk processing failed: {Error}", error);
                        throw new Exception(error);
                    }
                    
                    // Update streaming state
                    CanStartStreaming = chunkResult.CanStartStreaming;
                    HeaderParsed = chunkResult.HeaderParsed;
                    BufferedChunks = chunkResult.BufferCount;

                    // Set duration from WAV header when available (only set once)
                    if (chunkResult.Duration.HasValue && Duration == null)
                    {
                        Duration = chunkResult.Duration.Value;
                        _logger.LogInformation("Duration set from WAV header: {Duration:F2} seconds", Duration);
                    }
                    
                    // Start playback as soon as we can
                    if (!_streamingPlaybackStarted && CanStartStreaming)
                    {
                        var playbackResult = await _audioInterop.StartStreamingPlayback(PlayerId);
                        if (playbackResult.Success)
                        {
                            _streamingPlaybackStarted = true;
                            IsPlaying = true;
                            IsPaused = false;
                            IsLoaded = true; // Track is loaded and ready to play (even if still downloading)
                            ErrorMessage = null;
                            await NotifyStateChanged(); // Immediate notification for critical state change
                        }
                        else
                        {
                            var technicalError = $"Failed to start streaming playback: {playbackResult.Error}";
                            _logger.LogError("Failed to start playback: {Error}", technicalError);
                            ErrorMessage = StreamingErrorHandler.GetUserFriendlyMessage(technicalError);
                        }
                    }
                    
                    // Update progress
                    if (audio.ContentLength > 0)
                    {
                        LoadProgress = Math.Min(1.0, (double)totalBytesRead / audio.ContentLength);
                    }
                    
                    await ThrottledNotifyStateChanged();
                }
            } while (currentBytes > 0);
            
            // Mark as fully loaded
            LoadProgress = 1.0;
            await NotifyStateChanged();
        }
        catch (Exception ex)
        {
            StreamingErrorHandler.LogError(_logger, ex, "StreamAudioWithEarlyPlayback");
            ErrorMessage = StreamingErrorHandler.GetUserFriendlyMessage(ex.Message);
            LoadProgress = 0;
            IsLoaded = false;
            IsStreamingMode = false;
            await NotifyStateChanged();
            throw;
        }
        finally
        {
            if (buffer != null)
            {
                ArrayPool<byte>.Shared.Return(buffer);
            }
        }
    }

    /// <summary>
    /// In streaming mode, Stop fully resets to Idle state since audio data is consumed.
    /// This is equivalent to Unload for streaming playback.
    /// </summary>
    public override async Task Stop()
    {
        // In streaming mode, Stop = Unload (data is consumed, can't replay)
        await ResetToIdle();
    }

    /// <summary>
    /// Fully resets the player to Idle state, ready for a new track.
    /// </summary>
    public override async Task Unload()
    {
        await ResetToIdle();
    }

    /// <summary>
    /// Single method to reset all state - called by both Stop and Unload.
    /// </summary>
    private async Task ResetToIdle()
    {
        // 1. Cancel any ongoing streaming operation
        _streamingCancellation?.Cancel();
        _streamingCancellation?.Dispose();
        _streamingCancellation = null;

        // 2. Tell JS to stop and unload
        try
        {
            await _audioInterop.StopAsync(PlayerId);
            await _audioInterop.UnloadAsync(PlayerId);
        }
        catch
        {
            // Ignore JS errors during cleanup
        }

        // 3. Reset ALL state to Idle
        IsPlaying = false;
        IsPaused = false;
        IsLoaded = false;
        IsLoading = false;
        CurrentTime = 0;
        Duration = null;
        LoadProgress = 0;
        ErrorMessage = null;

        // 4. Reset streaming-specific state
        IsStreamingMode = false;
        CanStartStreaming = false;
        HeaderParsed = false;
        BufferedChunks = 0;
        _streamingPlaybackStarted = false;

        await NotifyStateChanged();
    }

    private async Task ThrottledNotifyStateChanged()
    {
        var now = DateTime.UtcNow;
        if ((now - _lastNotification).TotalMilliseconds >= NotificationThrottleMs)
        {
            _lastNotification = now;
            await NotifyStateChanged();
        }
    }

    private void AdaptBufferSize(int bytesRead, long readTimeMs)
    {
        // Adaptive buffer sizing based on network performance
        if (readTimeMs > 100) // Slow read (>100ms)
        {
            _consecutiveSlowReads++;
            if (_consecutiveSlowReads >= 3 && _currentBufferSize > MinBufferSize)
            {
                // Reduce buffer size for slow connections
                _currentBufferSize = Math.Max(MinBufferSize, _currentBufferSize / 2);
                _consecutiveSlowReads = 0;
            }
        }
        else if (readTimeMs < 20 && bytesRead == _currentBufferSize) // Fast read, buffer fully utilized
        {
            _consecutiveSlowReads = 0;
            if (_currentBufferSize < MaxBufferSize)
            {
                // Increase buffer size for fast connections
                _currentBufferSize = Math.Min(MaxBufferSize, _currentBufferSize * 2);
            }
        }
        else
        {
            _consecutiveSlowReads = 0;
        }
    }
}