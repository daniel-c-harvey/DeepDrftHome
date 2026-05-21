using DeepDrftPublic.Client.Services;
using Microsoft.AspNetCore.Components;

namespace DeepDrftPublic.Client.Controls.AudioPlayerBar;

public partial class SpectrumVisualizer : ComponentBase, IAsyncDisposable
{
    [Inject] public required AudioInteropService AudioInterop { get; set; }

    [CascadingParameter] public IStreamingPlayerService? PlayerService { get; set; }

    [Parameter] public int BucketCount { get; set; } = 32;

    private readonly string _instanceId = Guid.NewGuid().ToString();
    private double[] _spectrumData = Array.Empty<double>();
    private bool _isAnimating = false;
    private string? _playerId;

    private bool IsVisible => (PlayerService?.IsPlaying ?? false) || (PlayerService?.IsPaused ?? false) || _isAnimating;

    protected override void OnInitialized()
    {
        _spectrumData = new double[BucketCount];
    }

    protected override async Task OnParametersSetAsync()
    {
        // Provider re-renders cascade down to children and re-run OnParametersSet.
        // Pick up the player id once the cascade arrives, then drive animation
        // state from the parent's current IsPlaying — no callback wrapping needed.
        if (_playerId == null && PlayerService is AudioPlayerService baseService)
        {
            _playerId = baseService.PlayerId;
        }

        await UpdateAnimationState();
    }

    private async Task UpdateAnimationState()
    {
        if (string.IsNullOrEmpty(_playerId) || PlayerService == null) return;

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
