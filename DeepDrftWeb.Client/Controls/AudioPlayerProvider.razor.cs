using DeepDrftWeb.Client.Services;
using DeepDrftWeb.Client.Clients;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Logging;

namespace DeepDrftWeb.Client.Controls;

public partial class AudioPlayerProvider : ComponentBase
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
        _audioPlayerService.OnStateChanged = new EventCallback(this, StateHasChanged);
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
}