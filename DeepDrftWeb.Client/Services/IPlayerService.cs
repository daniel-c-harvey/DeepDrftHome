using DeepDrftModels.Entities;
using Microsoft.AspNetCore.Components;
using NetBlocks.Models;

namespace DeepDrftWeb.Client.Services;

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
    
    // Events for UI updates
    EventCallback? OnStateChanged { get; set; }
    EventCallback? OnTrackSelected { get; set; }
    
    // Control methods
    Task InitializeAsync();
    Task SelectTrack(TrackEntity track);
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
    Task SelectTrackStreaming(TrackEntity track);
}