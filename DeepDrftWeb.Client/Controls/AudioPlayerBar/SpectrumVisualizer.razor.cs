using DeepDrftWeb.Client.Services;
using Microsoft.AspNetCore.Components;

namespace DeepDrftWeb.Client.Controls.AudioPlayerBar;

public partial class SpectrumVisualizer : ComponentBase, IAsyncDisposable
{
    [Inject] public required AudioInteropService AudioInterop { get; set; }

    [CascadingParameter] public required IStreamingPlayerService PlayerService { get; set; }

    [Parameter] public int BucketCount { get; set; } = 32;

    private readonly string _instanceId = Guid.NewGuid().ToString();
    private double[] _spectrumData = Array.Empty<double>();
    private bool _isAnimating = false;
    private string? _playerId;
    private EventCallback? _originalOnStateChanged;

    private bool IsVisible => PlayerService.IsPlaying || PlayerService.IsPaused || _isAnimating;

    protected override void OnInitialized()
    {
        _spectrumData = new double[BucketCount];

        // Get the player ID from the service
        if (PlayerService is AudioPlayerService baseService)
        {
            _playerId = baseService.PlayerId;
        }

        // Chain into the existing OnStateChanged callback to detect play/pause
        _originalOnStateChanged = PlayerService.OnStateChanged;
        PlayerService.OnStateChanged = new EventCallback(this, async () =>
        {
            // Call original callback first
            if (_originalOnStateChanged.HasValue)
            {
                await _originalOnStateChanged.Value.InvokeAsync();
            }
            // Then update our animation state
            await UpdateAnimationState();
        });
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (firstRender)
        {
            // Initial check in case already playing
            await UpdateAnimationState();
        }
    }

    private async Task UpdateAnimationState()
    {
        if (string.IsNullOrEmpty(_playerId)) return;

        var shouldAnimate = PlayerService.IsPlaying;

        if (shouldAnimate && !_isAnimating)
        {
            await StartAnimation();
        }
        else if (!shouldAnimate && _isAnimating)
        {
            await StopAnimation();
        }
    }

    private async Task StartAnimation()
    {
        if (_isAnimating || string.IsNullOrEmpty(_playerId)) return;

        _isAnimating = true;
        await AudioInterop.StartSpectrumAnimationAsync(_playerId, _instanceId, OnSpectrumData);
    }

    private async Task StopAnimation()
    {
        if (!_isAnimating || string.IsNullOrEmpty(_playerId)) return;

        _isAnimating = false;
        await AudioInterop.StopSpectrumAnimationAsync(_playerId, _instanceId);

        // Clear the display
        Array.Clear(_spectrumData);
        await InvokeAsync(StateHasChanged);
    }

    private Task OnSpectrumData(double[] data)
    {
        if (data.Length > 0)
        {
            _spectrumData = data;
            InvokeAsync(StateHasChanged);
        }
        return Task.CompletedTask;
    }

    private double GetBarHeight(int index)
    {
        if (index >= _spectrumData.Length) return 0;

        // Scale to 0-100 percentage, with minimum height for visual appeal
        var value = _spectrumData[index];
        return Math.Max(2, value * 100);
    }

    public async ValueTask DisposeAsync()
    {
        await StopAnimation();
    }
}
