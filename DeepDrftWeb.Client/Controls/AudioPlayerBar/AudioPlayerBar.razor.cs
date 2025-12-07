using DeepDrftWeb.Client.Services;
using Microsoft.AspNetCore.Components;
using MudBlazor;
using MudBlazor.Services;

namespace DeepDrftWeb.Client.Controls.AudioPlayerBar;

public partial class AudioPlayerBar : ComponentBase, IAsyncDisposable
{
    [CascadingParameter] public required IStreamingPlayerService PlayerService { get; set; }
    [Inject] private IBrowserViewportService BrowserViewportService { get; set; } = default!;

    private bool _isMinimized = true;
    private bool _isSeeking = false;
    private double _seekPosition = 0;
    private bool _isDesktop = true;
    private Guid _viewportSubscriptionId;

    private bool IsLoaded => PlayerService.IsLoaded;
    private bool IsLoading => PlayerService.IsLoading;
    private bool IsStreaming => PlayerService.CanStartStreaming;
    private bool IsStreamingMode => PlayerService.IsStreamingMode;
    private bool IsPlaying => PlayerService.IsPlaying;
    private bool IsPaused => PlayerService.IsPaused;
    private double? Duration => PlayerService.Duration;
    private double Volume => PlayerService.Volume;
    private double LoadProgress => PlayerService.LoadProgress;
    private string? ErrorMessage => PlayerService.ErrorMessage;

    /// <summary>
    /// Display time - shows seek position while dragging, otherwise current playback time.
    /// </summary>
    private double DisplayTime => _isSeeking ? _seekPosition : PlayerService.CurrentTime;

    /// <summary>
    /// Seek is enabled once track is loaded AND duration is known (from WAV header).
    /// This allows seeking even during streaming, including seeking beyond buffered content.
    /// </summary>
    private bool CanSeek => IsLoaded && Duration.HasValue && Duration.Value > 0;

    protected override async Task OnInitializedAsync()
    {
        await base.OnInitializedAsync();
        // Set up EventCallback for track selection
        PlayerService.OnTrackSelected = new EventCallback(this, Expand);

        // Store the original OnStateChanged callback set by the provider
        var originalOnStateChanged = PlayerService.OnStateChanged;

        // Set up a wrapper that calls both the original callback and our StateHasChanged
        PlayerService.OnStateChanged = new EventCallback(this, async () =>
        {
            // Invoke the original callback (AudioPlayerProvider's StateHasChanged)
            if (originalOnStateChanged.HasValue)
            {
                await originalOnStateChanged.Value.InvokeAsync();
            }
            // Also trigger our own re-render
            await InvokeAsync(StateHasChanged);
        });
    }

    private async Task Expand()
    {
        if (_isMinimized)
        {
            _isMinimized = false;
            StateHasChanged();
        }
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

    private void OnSeekStart()
    {
        _isSeeking = true;
        _seekPosition = PlayerService.CurrentTime;
    }

    private void OnSeekChange(double position)
    {
        _seekPosition = position;
        StateHasChanged();
    }

    private async Task OnSeekEnd(double position)
    {
        _isSeeking = false;
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

    private async Task Close()
    {
        if (PlayerService.IsLoaded)
        {
            await PlayerService.Unload();
        }

        if (!_isMinimized)
        {
            _isMinimized = true;
            StateHasChanged();
        }
    }
    
    private string GetPlayIcon()
    {
        return IsPlaying ? Icons.Material.Filled.Pause : Icons.Material.Filled.PlayArrow;
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (firstRender)
        {
            var breakpoint = await BrowserViewportService.GetCurrentBreakpointAsync();
            _isDesktop = breakpoint >= Breakpoint.Sm;

            _viewportSubscriptionId = Guid.NewGuid();
            await BrowserViewportService.SubscribeAsync(
                _viewportSubscriptionId,
                args =>
                {
                    _isDesktop = args.Breakpoint >= Breakpoint.Sm;
                    InvokeAsync(StateHasChanged);
                },
                new ResizeOptions { NotifyOnBreakpointOnly = true },
                fireImmediately: true);

            StateHasChanged();
        }
    }

    public async ValueTask DisposeAsync()
    {
        await BrowserViewportService.UnsubscribeAsync(_viewportSubscriptionId);
    }
}