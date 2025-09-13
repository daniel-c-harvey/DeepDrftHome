using DeepDrftModels.Entities;
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
    event Action? OnStateChanged;
    event Events.EventAsync OnTrackSelected;
    
    // Control methods
    Task SelectTrack(TrackEntity track);
    Task Stop();
    Task Unload();
    Task TogglePlayPause();
    Task Seek(double position);
    Task SetVolume(double volume);
    void ClearError();
}