// DEPRECATED: This class has been replaced by AudioPlayerService
// TODO: Remove after testing new implementation
/*
using DeepDrftModels.Entities;
using NetBlocks.Models;

namespace DeepDrftWeb.Client.Services;

public class PlayerService : IPlayerService
{
    private AudioPlaybackEngine? _audioEngine;
    private bool _isInitialized = false;
    
    public PlayerService()
    {
        // Parameterless constructor - AudioPlaybackEngine will be set during initialization
    }
    
    // IPlayerService state properties with defensive checks
    public bool IsInitialized => _isInitialized;
    public bool IsLoaded => _isInitialized && _audioEngine?.IsLoaded == true;
    public bool IsLoading => _isInitialized && _audioEngine?.IsLoading == true;
    public bool IsPlaying => _isInitialized && _audioEngine?.IsPlaying == true;
    public bool IsPaused => _isInitialized && _audioEngine?.IsPaused == true;
    public double CurrentTime => _isInitialized ? _audioEngine?.CurrentTime ?? 0.0 : 0.0;
    public double? Duration => _isInitialized ? _audioEngine?.Duration : null;
    public double Volume => _isInitialized ? _audioEngine?.Volume ?? 0.8 : 0.8;
    public double LoadProgress => _isInitialized ? _audioEngine?.LoadProgress ?? 0.0 : 0.0;
    public string? ErrorMessage => _isInitialized ? _audioEngine?.ErrorMessage : null;
    
    public event Action? OnStateChanged;
    public event Events.EventAsync? OnTrackSelected;

    public async Task SelectTrack(TrackEntity track)
    {
        if (!_isInitialized)
        {
            await EnsureInitializedAsync();
        }
        
        // Immediately notify UI that track selection is happening
        OnStateChanged?.Invoke();
        
        if (OnTrackSelected != null) await OnTrackSelected.Invoke();
        
        if (_isInitialized && _audioEngine != null)
        {
            await _audioEngine.LoadTrack(track);
            // Force a state change to ensure UI reflects final loaded state
            OnStateChanged?.Invoke();
        }
    }

    public async Task Stop()
    {
        if (!_isInitialized || _audioEngine == null) return;
        
        await _audioEngine.Stop();
        OnStateChanged?.Invoke();
    }

    public async Task Unload()
    {
        if (!_isInitialized || _audioEngine == null) return;
        
        await _audioEngine.Unload();
        OnStateChanged?.Invoke();
    }
    
    public async Task TogglePlayPause()
    {
        if (!_isInitialized || _audioEngine == null) return;
        
        await _audioEngine.TogglePlayPause();
        OnStateChanged?.Invoke();
    }
    
    public async Task Seek(double position)
    {
        if (!_isInitialized || _audioEngine == null) return;
        
        await _audioEngine.OnSeek(position);
        OnStateChanged?.Invoke();
    }
    
    public async Task SetVolume(double volume)
    {
        if (!_isInitialized || _audioEngine == null) return;
        
        await _audioEngine.OnVolumeChange(volume);
        OnStateChanged?.Invoke();
    }
    
    public void ClearError()
    {
        if (!_isInitialized || _audioEngine == null) return;
        
        _audioEngine.ClearError();
        OnStateChanged?.Invoke();
    }
    
    public async Task InitializeAsync(AudioPlaybackEngine audioEngine)
    {
        if (_isInitialized) return;
        
        _audioEngine = audioEngine;
        
        try
        {
            await _audioEngine.InitializeAudioPlayer();
            
            // Wire up engine events to trigger state change notifications
            _audioEngine.OnProgressChanged += async _ => OnStateChanged?.Invoke();
            _audioEngine.OnPlaybackEnded += async () => OnStateChanged?.Invoke();
            _audioEngine.OnLoadChanged += async _ => OnStateChanged?.Invoke();
            
            _isInitialized = true;
            OnStateChanged?.Invoke();
        }
        catch (Exception ex)
        {
            // Log error but don't throw - allow UI to continue functioning
            Console.WriteLine($"Failed to initialize audio engine: {ex.Message}");
        }
    }
    
    private async Task EnsureInitializedAsync()
    {
        if (!_isInitialized && _audioEngine != null)
        {
            await InitializeAsync(_audioEngine);
        }
    }
}
*/