using DeepDrftPublic.Client.Services;
using DeepDrftPublic.Client.Clients;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Logging;

namespace DeepDrftPublic.Client.Controls;

public partial class AudioPlayerProvider : ComponentBase, IAsyncDisposable
{
    [Inject] public required AudioInteropService AudioInterop { get; set; }
    [Inject] public required TrackMediaClient TrackMediaClient { get; set; }
    [Inject] public required ILogger<StreamingAudioPlayerService> Logger { get; set; }

    private StreamingAudioPlayerService? _audioPlayerService;

    [Parameter] public RenderFragment? ChildContent { get; set; }

    protected override void OnInitialized()
    {
        // Create the service immediately (but don't initialize yet)
        _audioPlayerService = new StreamingAudioPlayerService(AudioInterop, TrackMediaClient, Logger);

        // Set up EventCallback to properly marshal UI updates back to UI thread
        // Use InvokeAsync to ensure proper Blazor render cycle triggering
        _audioPlayerService.OnStateChanged = new EventCallback(this, () => InvokeAsync(StateHasChanged));
        // OnTrackSelected will be set by individual child components that need it
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (firstRender && _audioPlayerService != null)
        {
            // Initialize the service after render when JavaScript is available
            await _audioPlayerService.InitializeAsync();
            StateHasChanged();
        }
    }

    /// <summary>
    /// Dispose the player on unmount so the JS setInterval driving progress
    /// callbacks no longer holds a DotNetObjectReference into a destroyed
    /// component (otherwise it throws every 100ms after navigation away).
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        if (_audioPlayerService != null)
        {
            await _audioPlayerService.DisposeAsync();
            _audioPlayerService = null;
        }
    }
}
