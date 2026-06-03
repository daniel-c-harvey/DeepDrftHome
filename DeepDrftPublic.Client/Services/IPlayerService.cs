using DeepDrftModels.DTOs;
using Microsoft.AspNetCore.Components;
using NetBlocks.Models;

namespace DeepDrftPublic.Client.Services;

public interface IPlayerService
{
    // State properties
    bool IsInitialized { get; }
    bool IsLoaded { get; }
    bool IsLoading { get; }
    bool IsPlaying { get; }
    bool IsPaused { get; }
    double CurrentTime { get; }
    double? Duration { get; }
    double Volume { get; }
    double LoadProgress { get; }
    string? ErrorMessage { get; }
    TrackDto? CurrentTrack { get; }

    // Events for UI updates
    EventCallback? OnStateChanged { get; set; }
    EventCallback? OnTrackSelected { get; set; }

    /// <summary>
    /// Multicast side-channel for state changes. The provider owns the single
    /// <see cref="OnStateChanged"/> EventCallback (it drives the provider re-render);
    /// cascade consumers that read state directly off this service — and so are not
    /// re-rendered by the provider's render when the cascade is <c>IsFixed</c> —
    /// subscribe here to re-render themselves. Fires on the same cadence as
    /// <see cref="OnStateChanged"/> (throttled to ~10/s during streaming).
    /// </summary>
    event Action? StateChanged;
    
    // Control methods
    Task InitializeAsync();
    Task SelectTrack(TrackDto track);
    Task Stop();
    Task Unload();
    Task TogglePlayPause();
    Task Seek(double position);
    Task SetVolume(double volume);
    Task ClearError();
}

public interface IStreamingPlayerService : IPlayerService
{
    // Streaming state properties
    bool IsStreamingMode { get; }
    bool CanStartStreaming { get; }
    bool HeaderParsed { get; }
    int BufferedChunks { get; }
    
    // Streaming control methods
    Task SelectTrackStreaming(TrackDto track);
}