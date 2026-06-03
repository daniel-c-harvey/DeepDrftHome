using DeepDrftPublic.Client.Services;
using Microsoft.AspNetCore.Components;
using MudBlazor;
using MudBlazor.Services;

namespace DeepDrftPublic.Client.Controls.AudioPlayerBar;

public partial class AudioPlayerBar : ComponentBase, IAsyncDisposable
{
    [CascadingParameter] public IStreamingPlayerService? PlayerService { get; set; }
    [Inject] public required IBrowserViewportService BrowserViewportService { get; set; }

    private bool _isMinimized = true;
    private bool _isSeeking = false;
    private double _seekPosition = 0;
    private bool _isDesktop = true;
    private Guid _viewportSubscriptionId;
    private IStreamingPlayerService? _subscribedService;

    private bool IsLoaded => PlayerService?.IsLoaded ?? false;
    private bool IsLoading => PlayerService?.IsLoading ?? false;
    private bool IsStreaming => PlayerService?.CanStartStreaming ?? false;
    private bool IsStreamingMode => PlayerService?.IsStreamingMode ?? false;
    private bool IsPlaying => PlayerService?.IsPlaying ?? false;
    private bool IsPaused => PlayerService?.IsPaused ?? false;
    private double? Duration => PlayerService?.Duration;
    private double Volume => PlayerService?.Volume ?? 0;
    private double LoadProgress => PlayerService?.LoadProgress ?? 0;
    private string? ErrorMessage => PlayerService?.ErrorMessage;

    /// <summary>
    /// Display time - shows seek position while dragging, otherwise current playback time.
    /// </summary>
    private double DisplayTime => _isSeeking ? _seekPosition : (PlayerService?.CurrentTime ?? 0);

    /// <summary>
    /// Seek is enabled once track is loaded AND duration is known (from WAV header).
    /// This allows seeking even during streaming, including seeking beyond buffered content.
    /// </summary>
    private bool CanSeek => IsLoaded && Duration.HasValue && Duration.Value > 0;

    protected override void OnParametersSet()
    {
        // PlayerService is cascaded by AudioPlayerProvider; once it arrives,
        // wire our track-selection handler. The provider owns OnStateChanged —
        // we intentionally do NOT wrap or replace it. Because the cascade is
        // IsFixed, the provider's re-render does NOT reliably re-render this bar
        // (it has no incoming parameters that change), so we subscribe to the
        // multicast StateChanged side-channel to re-render ourselves.
        if (PlayerService != null && !ReferenceEquals(PlayerService, _subscribedService))
        {
            if (_subscribedService != null)
                _subscribedService.StateChanged -= OnPlayerStateChanged;

            PlayerService.OnTrackSelected = new EventCallback(this, Expand);
            PlayerService.StateChanged += OnPlayerStateChanged;
            _subscribedService = PlayerService;
        }
    }

    private void OnPlayerStateChanged() => InvokeAsync(StateHasChanged);

    private async Task Expand()
    {
        if (_isMinimized)
        {
            _isMinimized = false;
            StateHasChanged();
        }
    }

    private async Task TogglePlayPause()
    {
        if (PlayerService == null) return;
        await PlayerService.TogglePlayPause();
    }

    private async Task Stop()
    {
        if (PlayerService == null) return;
        await PlayerService.Stop();
    }

    private void OnSeekStart()
    {
        if (PlayerService == null) return;
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
        if (PlayerService == null) return;
        _isSeeking = false;
        await PlayerService.Seek(position);
    }

    private async Task OnVolumeChange(double volume)
    {
        if (PlayerService == null) return;
        await PlayerService.SetVolume(volume);
    }

    private void ClearError()
    {
        PlayerService?.ClearError();
    }

    private void ToggleMinimized()
    {
        _isMinimized = !_isMinimized;
        StateHasChanged();
    }

    private async Task Close()
    {
        if (PlayerService != null && PlayerService.IsLoaded)
        {
            await PlayerService.Unload();
        }

        if (!_isMinimized)
        {
            _isMinimized = true;
            StateHasChanged();
        }
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
        if (_subscribedService != null)
        {
            _subscribedService.StateChanged -= OnPlayerStateChanged;
            _subscribedService = null;
        }
        await BrowserViewportService.UnsubscribeAsync(_viewportSubscriptionId);
    }
}