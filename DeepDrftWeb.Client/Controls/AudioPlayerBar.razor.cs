using Microsoft.AspNetCore.Components;
using DeepDrftWeb.Client.Services;
using MudBlazor;

namespace DeepDrftWeb.Client.Controls;

public partial class AudioPlayerBar : ComponentBase, IAsyncDisposable
{
    [Parameter] public string? AudioUrl { get; set; }
    [Parameter] public bool ShowLoadProgress { get; set; } = true;
    [Parameter] public EventCallback<double> OnProgressChanged { get; set; }
    [Parameter] public EventCallback OnPlaybackEnded { get; set; }

    [Inject] public required AudioInteropService AudioInterop { get; set; }
    
    private string PlayerId = Guid.NewGuid().ToString();
    private bool IsLoaded = false;
    private bool IsPlaying = false;
    private bool IsPaused = false;
    private double CurrentTime = 0;
    private double Duration = 0;
    private double Volume = 0.8;
    private double LoadProgress = 0;
    private string? ErrorMessage;
    private Timer? progressTimer;

    protected override async Task OnInitializedAsync()
    {
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
    }

    protected override async Task OnParametersSetAsync()
    {
        if (IsLoaded) return;

        try
        {
            AudioLoadResult? loadResult = null;

            if (!string.IsNullOrEmpty(AudioUrl))
            {
                loadResult = await AudioInterop.LoadAudioFromUrlAsync(PlayerId, AudioUrl);
            }

            if (loadResult?.Success == true)
            {
                IsLoaded = true;
                Duration = loadResult.Duration;
                LoadProgress = loadResult.LoadProgress;
                ErrorMessage = null;
                StateHasChanged();
            }
            else
            {
                ErrorMessage = $"Failed to load audio: {loadResult?.Error ?? "No audio source provided"}";
            }
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Error loading audio: {ex.Message}";
        }
    }

    private async Task TogglePlayPause()
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

        StateHasChanged();
    }

    private async Task Stop()
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

        StateHasChanged();
    }

    private async Task OnSeek(double position)
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

        StateHasChanged();
    }

    private async Task OnVolumeChange(double volume)
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
                    StateHasChanged();
                }
            }
            catch (Exception ex)
            {
                ErrorMessage = $"Error setting volume: {ex.Message}";
                StateHasChanged();
            }
        }
    }

    private async Task OnProgress(double currentTime)
    {
        CurrentTime = currentTime;
        if (OnProgressChanged.HasDelegate)
        {
            await OnProgressChanged.InvokeAsync(currentTime);
        }
        await InvokeAsync(StateHasChanged);
    }

    private async Task OnPlaybackEnd()
    {
        IsPlaying = false;
        IsPaused = false;
        CurrentTime = 0;
        
        if (OnPlaybackEnded.HasDelegate)
        {
            await OnPlaybackEnded.InvokeAsync();
        }
        
        await InvokeAsync(StateHasChanged);
    }

    private async Task OnLoadProgress(double progress)
    {
        LoadProgress = progress;
        await InvokeAsync(StateHasChanged);
    }

    private string GetPlayIcon()
    {
        return IsPlaying ? Icons.Material.Filled.Pause : Icons.Material.Filled.PlayArrow;
    }

    private string GetVolumeIcon()
    {
        if (Volume == 0) return Icons.Material.Filled.VolumeOff;
        if (Volume < 0.5) return Icons.Material.Filled.VolumeDown;
        return Icons.Material.Filled.VolumeUp;
    }

    private static string FormatTime(double seconds)
    {
        var timeSpan = TimeSpan.FromSeconds(seconds);
        return timeSpan.ToString(timeSpan.TotalHours >= 1 ? @"h\:mm\:ss" : @"m\:ss");
    }

    private void ClearError()
    {
        ErrorMessage = null;
        StateHasChanged();
    }

    public async ValueTask DisposeAsync()
    {
        progressTimer?.Dispose();
        
        if (IsLoaded)
        {
            await AudioInterop.DisposePlayerAsync(PlayerId);
        }
    }
}