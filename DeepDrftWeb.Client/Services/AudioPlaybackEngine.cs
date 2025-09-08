using DeepDrftModels.Entities;
using DeepDrftWeb.Client.Clients;
using NetBlocks.Models;

namespace DeepDrftWeb.Client.Services;

public class AudioPlaybackEngine : IAsyncDisposable
{
    public event Events.EventAsync<double>? OnProgressChanged;
    public event Events.EventAsync? OnPlaybackEnded;
    
    public required TrackMediaClient Client { get; set; }
    public required AudioInteropService AudioInterop { get; set; }

    public string PlayerId { get; private set; } = Guid.NewGuid().ToString();
    public bool IsInitialized { get; private set; } = false;
    public bool IsLoaded { get; private set; } = false;
    public bool IsPlaying { get; private set; } = false;
    public bool IsPaused { get; private set; } = false;
    public double CurrentTime { get; private set; } = 0;
    public double Duration { get; private set; } = 0;
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
        if (IsLoaded) return;

        try
        {
            AudioOperationResult? loadResult = await AudioInterop.InitializeBufferedPlayerAsync(PlayerId);
            TrackMediaResponse? audio = await Client.GetTrackMedia(track.EntryKey);

            if (loadResult?.Success == true)
            {
                IsLoaded = true;
                ErrorMessage = null;
                await StreamAndPlay(audio);
            }
            else
            {
                ErrorMessage = $"Failed to play audio: {loadResult?.Error ?? "No audio source provided"}";
            }
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Error loading audio: {ex.Message}";
        }
    }

    private async Task StreamAndPlay(TrackMediaResponse audio)
    {
        int bytesRead = 0;
        do
        {
            var buffer = new byte[8 * 1024];
            int newBytes = await audio.Stream.ReadAsync(buffer, 0, buffer.Length);
            bytesRead += newBytes;
            if (bytesRead == 0) break;
            await AudioInterop.AppendAudioBlockAsync(PlayerId, buffer);
        } while (bytesRead < audio.ContentLength);
        await AudioInterop.FinalizeAudioBufferAsync(PlayerId);
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