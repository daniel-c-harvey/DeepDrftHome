using DeepDrftModels.Entities;
using DeepDrftWeb.Client.Clients;
using Microsoft.AspNetCore.Components;
using DeepDrftWeb.Client.Services;
using MudBlazor;

namespace DeepDrftWeb.Client.Controls;

public partial class AudioPlayerBar : ComponentBase, IAsyncDisposable
{
    [Parameter] public bool ShowLoadProgress { get; set; } = true;
    
    [Parameter] public required AudioPlaybackEngine AudioPlaybackEngine { get; set; }
    
    private bool IsLoaded => AudioPlaybackEngine.IsLoaded;
    private bool IsPlaying => AudioPlaybackEngine.IsPlaying;
    private bool IsPaused => AudioPlaybackEngine.IsPaused;
    private double CurrentTime => AudioPlaybackEngine.CurrentTime;
    private double Duration => AudioPlaybackEngine.Duration;
    private double Volume => AudioPlaybackEngine.Volume;
    private double LoadProgress => AudioPlaybackEngine.LoadProgress;
    private string? ErrorMessage => AudioPlaybackEngine.ErrorMessage;

    protected override async Task OnInitializedAsync()
    {
        await base.OnInitializedAsync();

        AudioPlaybackEngine.OnProgressChanged += async _ => StateHasChanged();
        AudioPlaybackEngine.OnPlaybackEnded += async () => await Stop(); // TODO unload the engine track instead of stopping
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

    private async Task TogglePlayPause()
    {
        await AudioPlaybackEngine.TogglePlayPause();
        StateHasChanged();
    }

    private async Task Stop()
    {
        await AudioPlaybackEngine.Stop();
        StateHasChanged();
    }

    private async Task OnSeek(double position)
    {
        await AudioPlaybackEngine.OnSeek(position);
        StateHasChanged();
    }

    private async Task OnVolumeChange(double volume)
    {
        await AudioPlaybackEngine.OnVolumeChange(volume);
        StateHasChanged();
    }
    
    private void ClearError()
    {
        AudioPlaybackEngine.ClearError();
        StateHasChanged();
    }
    
    public async ValueTask DisposeAsync()
    {
        await AudioPlaybackEngine.DisposeAsync();
    }
}