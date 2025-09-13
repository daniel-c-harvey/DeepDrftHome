using DeepDrftWeb.Client.Services;
using DeepDrftWeb.Client.Clients;
using Microsoft.AspNetCore.Components;

namespace DeepDrftWeb.Client.Controls;

public partial class AudioPlayerProvider : ComponentBase
{
    [Inject] public required AudioInteropService AudioInterop { get; set; }
    [Inject] public required TrackMediaClient TrackMediaClient { get; set; }
    
    private AudioPlayerService? _audioPlayerService;
    
    [Parameter] public RenderFragment? ChildContent { get; set; }
    
    protected override void OnInitialized()
    {
        // Create the service immediately (but don't initialize yet)
        _audioPlayerService = new AudioPlayerService(AudioInterop, TrackMediaClient);
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