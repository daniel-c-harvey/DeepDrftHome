using DeepDrftModels.Entities;
using DeepDrftWeb.Client.Clients;
using Microsoft.AspNetCore.Components;
using DeepDrftWeb.Client.Services;
using MudBlazor;

namespace DeepDrftWeb.Client.Controls;

public partial class AudioPlayerBar : ComponentBase
{
    [CascadingParameter] public required IPlayerService PlayerService { get; set; }
    [Parameter] public bool ShowLoadProgress { get; set; } = true;
    private bool _isMinimized = true;
    
    private bool IsLoaded => PlayerService.IsLoaded;
    private bool IsPlaying => PlayerService.IsPlaying;
    private bool IsPaused => PlayerService.IsPaused;
    private double CurrentTime => PlayerService.CurrentTime;
    private double? Duration => PlayerService.Duration;
    private double Volume => PlayerService.Volume;
    private double LoadProgress => PlayerService.LoadProgress;
    private string? ErrorMessage => PlayerService.ErrorMessage;

    protected override async Task OnInitializedAsync()
    {
        await base.OnInitializedAsync();

        PlayerService.OnStateChanged += StateHasChanged;
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
        await PlayerService.TogglePlayPause();
    }

    private async Task Stop()
    {
        await PlayerService.Stop();
    }

    private async Task OnSeek(double position)
    {
        await PlayerService.Seek(position);
    }

    private async Task OnVolumeChange(double volume)
    {
        await PlayerService.SetVolume(volume);
    }
    
    private void ClearError()
    {
        PlayerService.ClearError();
    }
    
    private void ToggleMinimized()
    {
        _isMinimized = !_isMinimized;
        StateHasChanged();
    }
    
}