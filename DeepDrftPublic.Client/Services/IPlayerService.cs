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