using DeepDrftModels.Entities;
using DeepDrftWeb.Client.Clients;
using NetBlocks.Models;

namespace DeepDrftWeb.Client.Services;

public class AudioPlaybackEngine : IAsyncDisposable
{
    public event Events.EventAsync<double>? OnProgressChanged;
    public event Events.EventAsync<double>? OnLoadChanged;
    public event Events.EventAsync? OnPlaybackEnded;
    
    public required TrackMediaClient Client { get; set; }
    public required AudioInteropService AudioInterop { get; set; }

    public string PlayerId { get; private set; } = Guid.NewGuid().ToString();
    public bool IsInitialized { get; private set; } = false;
    public bool IsLoaded { get; private set; } = false;
    public bool IsLoading { get; private set; } = false;
    public bool IsPlaying { get; private set; } = false;
    public bool IsPaused { get; private set; } = false;
    public double CurrentTime { get; private set; } = 0;
    public double? Duration { get; private set; } = null;
    public double Volume { get; private set; } = 0.8;
    public double LoadProgress { get; private set; } = 0;
    public string? ErrorMessage { get; private set; }

    public AudioPlaybackEngine(AudioInteropService audioInterop, TrackMediaClient client)
    {
        AudioInterop = audioInterop;
        Client = client;
    }

    public async Task InitializeAudioPlayer()
    {
        if (IsInitialized) return;

        var result = await AudioInterop.CreatePlayerAsync(PlayerId);
        if (!result.Success)
        {
            ErrorMessage = $"Failed to initialize audio player: {result.Error}";
            return;
        }

        await AudioInterop.SetOnProgressCallbackAsync(PlayerId, OnProgress);
        await AudioInterop.SetOnEndCallbackAsync(PlayerId, OnPlaybackEnd);
        await AudioInterop.SetOnLoadProgressCallbackAsync(PlayerId, OnLoadProgress);
        
        await AudioInterop.SetVolumeAsync(PlayerId, Volume);
        
        IsInitialized = true;
    }

    public async Task LoadTrack(TrackEntity track)
    {
        TrackMediaResponse? audio = null;
        try
        {
            // Immediately reset state to indicate loading has started
            ErrorMessage = null;
            LoadProgress = 0;
            IsLoaded = false;
            IsLoading = true;
            Duration = null;
            CurrentTime = 0;

            // Trigger load event immediately to show loading state in UI
            if (OnLoadChanged != null) await OnLoadChanged.Invoke(0);

            if (IsPlaying || IsPaused)
            {
                // If we were playing/paused, unload the current track
                await Unload();
            }

            AudioOperationResult? loadResult = await AudioInterop.InitializeBufferedPlayerAsync(PlayerId);
            if (loadResult?.Success != true)
            {
                ErrorMessage = $"Failed to initialize audio buffer: {loadResult?.Error ?? "Unknown error"}";
                return;
            }

            var mediaResult = await Client.GetTrackMedia(track.EntryKey);
            if (!mediaResult.Success)
            {
                ErrorMessage = mediaResult.GetMessage();
                return;
            }

            if (mediaResult.Value == null)
            {
                ErrorMessage = "No audio returned from server";
                return;
            }
            audio = mediaResult.Value;
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Error loading audio: {ex.Message}";
            LoadProgress = 0;
            IsLoaded = false;
        }
        finally
        {
            IsLoading = false;
        }

        try
        {
            if (audio == null) return;
            
            await StreamAndPlay(audio);
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Error streaming audio: {ex.Message}";
        }
    }

    private async Task StreamAndPlay(TrackMediaResponse audio)
    {
        try
        {
            const int bufferSize = 32 * 1024; // Increased buffer size for better performance
            long totalBytesRead = 0;
            int currentBytes;
            
            do
            {
                var buffer = new byte[bufferSize];
                currentBytes = await audio.Stream.ReadAsync(buffer, 0, buffer.Length);
                
                if (currentBytes > 0)
                {
                    totalBytesRead += currentBytes;
                    
                    // Resize buffer if we didn't read the full amount
                    if (currentBytes < bufferSize)
                    {
                        var trimmedBuffer = new byte[currentBytes];
                        Array.Copy(buffer, trimmedBuffer, currentBytes);
                        buffer = trimmedBuffer;
                    }
                    
                    var appendResult = await AudioInterop.AppendAudioBlockAsync(PlayerId, buffer);
                    if (!appendResult.Success)
                    {
                        throw new Exception($"Failed to append audio block: {appendResult.Error}");
                    }
                    
                    // Update progress during streaming
                    if (audio.ContentLength > 0)
                    {
                        LoadProgress = Math.Min(1.0, (double)totalBytesRead / audio.ContentLength);
                        if (OnLoadChanged != null) await OnLoadChanged.Invoke(LoadProgress);   
                    }
                }
            } while (currentBytes > 0);
            
            // Finalize the buffer and update metadata
            var finalizeResult = await AudioInterop.FinalizeAudioBufferAsync(PlayerId);
            if (!finalizeResult.Success)
            {
                throw new Exception($"Failed to finalize audio buffer: {finalizeResult.Error}");
            }
            
            // Update engine state with audio metadata
            Duration = finalizeResult.Duration;
            LoadProgress = 1.0;
            IsLoaded = true;
            ErrorMessage = null;
            
            // Trigger final load completion event
            if (OnLoadChanged != null) await OnLoadChanged.Invoke(1.0);
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Error streaming audio: {ex.Message}";
            LoadProgress = 0;
            IsLoaded = false;
            throw;
        }
    }

    public async Task TogglePlayPause()
    {
        if (!IsLoaded) return;

        try
        {
            AudioOperationResult result;

            if (IsPlaying)
            {
                result = await AudioInterop.PauseAsync(PlayerId);
                if (result.Success)
                {
                    IsPlaying = false;
                    IsPaused = true;
                }
            }
            else
            {
                result = await AudioInterop.PlayAsync(PlayerId);
                if (result.Success)
                {
                    IsPlaying = true;
                    IsPaused = false;
                }
            }

            if (!result.Success)
            {
                ErrorMessage = $"Playback error: {result.Error}";
            }
            else
            {
                ErrorMessage = null;
            }
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Error controlling playback: {ex.Message}";
        }
    }

    public async Task Stop()
    {
        if (!IsLoaded) return;

        try
        {
            var result = await AudioInterop.StopAsync(PlayerId);
            if (result.Success)
            {
                IsPlaying = false;
                IsPaused = false;
                CurrentTime = 0;
                ErrorMessage = null;
            }
            else
            {
                ErrorMessage = $"Stop error: {result.Error}";
            }
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Error stopping playback: {ex.Message}";
        }
    }

    public async Task Unload()
    {
        if (!IsLoaded) return;
        
        try
        {
            await Stop();
            var result = await AudioInterop.UnloadAsync(PlayerId);
            if (result.Success)
            {
                IsPlaying = false;
                IsPaused = false;
                CurrentTime = 0;
                Duration = null;
                LoadProgress = 0;
                IsLoaded = false;
                ErrorMessage = null;
            }
            else
            {
                ErrorMessage = $"Unload error: {result.Error}";
            }
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Error unloading track: {ex.Message}";
        }
    }

    public async Task OnSeek(double position)
    {
        if (!IsLoaded) return;

        try
        {
            var result = await AudioInterop.SeekAsync(PlayerId, position);
            if (result.Success)
            {
                CurrentTime = position;
                ErrorMessage = null;
            }
            else
            {
                ErrorMessage = $"Seek error: {result.Error}";
            }
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Error seeking: {ex.Message}";
        }
    }

    public async Task OnVolumeChange(double volume)
    {
        Volume = volume;

        if (IsLoaded)
        {
            try
            {
                var result = await AudioInterop.SetVolumeAsync(PlayerId, volume);
                if (!result.Success)
                {
                    ErrorMessage = $"Volume error: {result.Error}";
                }
            }
            catch (Exception ex)
            {
                ErrorMessage = $"Error setting volume: {ex.Message}";
            }
        }
    }

    private async Task OnProgress(double currentTime)
    {
        CurrentTime = currentTime;
        if (OnProgressChanged != null)
        {
            await OnProgressChanged(currentTime);
        }
    }

    private async Task OnPlaybackEnd()
    {
        IsPlaying = false;
        IsPaused = false;
        CurrentTime = 0;
        
        if (OnPlaybackEnded != null)
        {
            await OnPlaybackEnded();
        }
    }

    private async Task OnLoadProgress(double progress)
    {
        LoadProgress = progress;
    }
    
    public void ClearError()
    {
        ErrorMessage = null;
    }

    public async ValueTask DisposeAsync()
    {
        await AudioInterop.DisposePlayerAsync(PlayerId);
    }
}