using DeepDrftModels.DTOs;
using DeepDrftPublic.Client.Clients;
using Microsoft.AspNetCore.Components;
using NetBlocks.Models;

namespace DeepDrftPublic.Client.Services;

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
    /// <summary>
    /// The currently selected track. In the streaming subclass this property is managed
    /// exclusively by <see cref="StreamingAudioPlayerService"/>: set in
    /// <c>LoadTrackStreaming</c> after <c>ResetToIdle</c> clears it, and cleared again
    /// by <c>ResetToIdle</c> on stop/unload/dispose. Base-class subclasses that take the
    /// <see cref="SelectTrack"/>/<see cref="Unload"/> path are responsible for managing
    /// it themselves.
    /// </summary>
    public TrackDto? CurrentTrack { get; protected set; }

    // Events
    public EventCallback? OnStateChanged { get; set; }
    public EventCallback? OnTrackSelected { get; set; }

    /// <inheritdoc />
    public event Action? StateChanged;

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

    /// <summary>
    /// Selecting a track is only supported through the streaming path. The former
    /// base buffered implementation drove JS no-ops (<c>initializeBufferedPlayer</c> /
    /// <c>appendAudioBlock</c> / <c>finalizeAudioBuffer</c>) and silently played
    /// silence. Subclasses must override with a real load path (see
    /// <see cref="StreamingAudioPlayerService.SelectTrack"/>).
    /// </summary>
    public virtual Task SelectTrack(TrackDto track) =>
        throw new NotSupportedException(
            "The base buffered player path is not implemented. Use a streaming player (SelectTrackStreaming).");

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

    public virtual async Task Seek(double position)
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
        IsLoaded = false;
        CurrentTime = 0;
        Duration = null;
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
        StateChanged?.Invoke();
    }

    protected async Task NotifyTrackSelected()
    {
        if (OnTrackSelected.HasValue)
            await OnTrackSelected.Value.InvokeAsync();
    }

    public virtual async ValueTask DisposeAsync()
    {
        if (IsInitialized)
        {
            await _audioInterop.DisposePlayerAsync(PlayerId);
        }
    }
}