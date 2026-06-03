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

    private IStreamingPlayerService? _audioPlayerService;

    [Parameter] public RenderFragment? ChildContent { get; set; }

    protected override void OnInitialized()
    {
        // Create the service immediately (but don't initialize yet).
        // The base class lazily initializes on first track selection via
        // EnsureInitializedAsync — that path is correct because audio contexts
        // require a user gesture anyway. Initializing eagerly here causes 4+
        // SignalR round-trips before any content is stable.
        _audioPlayerService = new StreamingAudioPlayerService(AudioInterop, TrackMediaClient, Logger);

        // Provider is the SOLE owner of OnStateChanged. When the service fires,
        // the provider re-renders, which cascades to its children automatically.
        // Children must not wrap or replace this callback.
        _audioPlayerService.OnStateChanged = new EventCallback(this, () => InvokeAsync(StateHasChanged));
        // OnTrackSelected will be set by individual child components that need it
    }

    /// <summary>
    /// Dispose the player on unmount so the JS setInterval driving progress
    /// callbacks no longer holds a DotNetObjectReference into a destroyed
    /// component (otherwise it throws every 100ms after navigation away).
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        if (_audioPlayerService is IAsyncDisposable disposable)
        {
            await disposable.DisposeAsync();
        }
        _audioPlayerService = null;
    }
}
