using DeepDrftModels.Entities;
using DeepDrftWeb.Client.Clients;
using Microsoft.AspNetCore.Components;
using NetBlocks.Models;

namespace DeepDrftWeb.Client.Services;

public abstract class AudioPlayerService : IPlayerService, IAsyncDisposable
{
    protected readonly AudioInteropService _audioInterop;
    protected readonly TrackMediaClient _trackMediaClient;
    
    public string PlayerId { get; private set; } = Guid.NewGuid().ToString();
    
    // State properties
    public bool IsInitialized { get; protected set; } = false;
    public bool IsLoaded { get; protected set; } = false;
    public bool IsLoading { get; protected set; } = false;
    public bool IsPlaying { get; protected set; } = false;
    public bool IsPaused { get; protected set; } = false;
    public double CurrentTime { get; protected set; } = 0;
    public double? Duration { get; protected set; } = null;
    public double Volume { get; protected set; } = 0.8;
    public double LoadProgress { get; protected set; } = 0;
    public string? ErrorMessage { get; protected set; }

    // Events
    public EventCallback? OnStateChanged { get; set; }
    public EventCallback? OnTrackSelected { get; set; }

    protected AudioPlayerService(AudioInteropService audioInterop, TrackMediaClient trackMediaClient)
    {
        _audioInterop = audioInterop;
        _trackMediaClient = trackMediaClient;
    }

    public async Task InitializeAsync()
    {
        if (IsInitialized) return;

        try
        {
            var result = await _audioInterop.CreatePlayerAsync(PlayerId);
            if (!result.Success)
            {
                ErrorMessage = $"Failed to initialize audio player: {result.Error}";
                await NotifyStateChanged();
                return;
            }

            await _audioInterop.SetOnProgressCallbackAsync(PlayerId, OnProgressCallback);
            await _audioInterop.SetOnEndCallbackAsync(PlayerId, OnPlaybackEndCallback);
            
            await _audioInterop.SetVolumeAsync(PlayerId, Volume);
            
            IsInitialized = true;
            ErrorMessage = null;
            await NotifyStateChanged();
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Failed to initialize audio player: {ex.Message}";
            await NotifyStateChanged();
        }
    }

    public virtual async Task SelectTrack(TrackEntity track)
    {
        await EnsureInitializedAsync();
        
        await NotifyStateChanged();
        
        if (OnTrackSelected.HasValue)
            await OnTrackSelected.Value.InvokeAsync();
        
        await LoadTrack(track);
        await NotifyStateChanged();
    }

    private async Task LoadTrack(TrackEntity track)
    {
        try
        {
            if (IsLoading) return;
            
            if (IsPlaying || IsPaused)
            {
                await Unload();
            }
            
            // Reset state to indicate loading has started
            ErrorMessage = null;
            LoadProgress = 0;
            IsLoaded = false;
            IsLoading = true;
            Duration = null;
            CurrentTime = 0;
            await NotifyStateChanged();

            var loadResult = await _audioInterop.InitializeBufferedPlayerAsync(PlayerId);
            if (loadResult?.Success != true)
            {
                ErrorMessage = $"Failed to initialize audio buffer: {loadResult?.Error ?? "Unknown error"}";
                return;
            }

            var mediaResult = await _trackMediaClient.GetTrackMedia(track.EntryKey);
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
            
            TrackMediaResponse audio = mediaResult.Value;
            await StreamAudio(audio);
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
            await NotifyStateChanged();
        }
    }

    private async Task StreamAudio(TrackMediaResponse audio)
    {
        try
        {
            const int bufferSize = 32 * 1024;
            long totalBytesRead = 0;
            int currentBytes;
            
            do
            {
                var buffer = new byte[bufferSize];
                currentBytes = await audio.Stream.ReadAsync(buffer, 0, buffer.Length);
                
                if (currentBytes > 0)
                {
                    totalBytesRead += currentBytes;
                    
                    if (currentBytes < bufferSize)
                    {
                        var trimmedBuffer = new byte[currentBytes];
                        Array.Copy(buffer, trimmedBuffer, currentBytes);
                        buffer = trimmedBuffer;
                    }
                    
                    var appendResult = await _audioInterop.AppendAudioBlockAsync(PlayerId, buffer);
                    if (!appendResult.Success)
                    {
                        throw new Exception($"Failed to append audio block: {appendResult.Error}");
                    }
                    
                    if (audio.ContentLength > 0)
                    {
                        LoadProgress = Math.Min(1.0, (double)totalBytesRead / audio.ContentLength);
                        await NotifyStateChanged();
                    }
                }
            } while (currentBytes > 0);
            
            var finalizeResult = await _audioInterop.FinalizeAudioBufferAsync(PlayerId);
            if (!finalizeResult.Success)
            {
                throw new Exception($"Failed to finalize audio buffer: {finalizeResult.Error}");
            }
            
            Duration = finalizeResult.Duration;
            LoadProgress = 1.0;
            IsLoaded = true;
            ErrorMessage = null;
            await NotifyStateChanged();
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Error streaming audio: {ex.Message}";
            LoadProgress = 0;
            IsLoaded = false;
            await NotifyStateChanged();
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
                result = await _audioInterop.PauseAsync(PlayerId);
                if (result.Success)
                {
                    IsPlaying = false;
                    IsPaused = true;
                }
            }
            else
            {
                result = await _audioInterop.PlayAsync(PlayerId);
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
            
            await NotifyStateChanged();
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Error controlling playback: {ex.Message}";
            await NotifyStateChanged();
        }
    }

    public virtual async Task Stop()
    {
        if (!IsLoaded) return;

        try
        {
            var result = await _audioInterop.StopAsync(PlayerId);
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
            
            await NotifyStateChanged();
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Error stopping playback: {ex.Message}";
            await NotifyStateChanged();
        }
    }

    public virtual async Task Unload()
    {
        if (!IsLoaded) return;
        
        try
        {
            await Stop();
            var result = await _audioInterop.UnloadAsync(PlayerId);
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
            
            await NotifyStateChanged();
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Error unloading track: {ex.Message}";
            await NotifyStateChanged();
        }
    }

    public async Task Seek(double position)
    {
        if (!IsLoaded) return;

        try
        {
            var result = await _audioInterop.SeekAsync(PlayerId, position);
            if (result.Success)
            {
                CurrentTime = position;
                ErrorMessage = null;
            }
            else
            {
                ErrorMessage = $"Seek error: {result.Error}";
            }
            
            await NotifyStateChanged();
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Error seeking: {ex.Message}";
            await NotifyStateChanged();
        }
    }

    public async Task SetVolume(double volume)
    {
        Volume = volume;

        if (IsLoaded)
        {
            try
            {
                var result = await _audioInterop.SetVolumeAsync(PlayerId, volume);
                if (!result.Success)
                {
                    ErrorMessage = $"Volume error: {result.Error}";
                }
                else
                {
                    ErrorMessage = null;
                }
            }
            catch (Exception ex)
            {
                ErrorMessage = $"Error setting volume: {ex.Message}";
            }
        }
        
        await NotifyStateChanged();
    }

    public async Task ClearError()
    {
        ErrorMessage = null;
        await NotifyStateChanged();
    }

    private async Task OnProgressCallback(double currentTime)
    {
        CurrentTime = currentTime;
        await NotifyStateChanged();
    }

    private async Task OnPlaybackEndCallback()
    {
        IsPlaying = false;
        IsPaused = false;
        CurrentTime = 0;
        await NotifyStateChanged();
    }


    protected async Task EnsureInitializedAsync()
    {
        if (!IsInitialized)
        {
            await InitializeAsync();
        }
    }

    protected async Task NotifyStateChanged()
    {
        if (OnStateChanged.HasValue)
            await OnStateChanged.Value.InvokeAsync();
    }

    protected async Task NotifyTrackSelected()
    {
        if (OnTrackSelected.HasValue)
            await OnTrackSelected.Value.InvokeAsync();
    }

    public async ValueTask DisposeAsync()
    {
        if (IsInitialized)
        {
            await _audioInterop.DisposePlayerAsync(PlayerId);
        }
    }
}