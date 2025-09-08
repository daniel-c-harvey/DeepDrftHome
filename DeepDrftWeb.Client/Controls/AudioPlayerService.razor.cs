using DeepDrftWeb.Client.Services;
using Microsoft.AspNetCore.Components;
using DeepDrftModels.Entities;

namespace DeepDrftWeb.Client.Controls;

public partial class AudioPlayerService : ComponentBase
{
    [Inject] public required AudioPlaybackEngine AudioPlaybackEngine { get; set; }
    
    private readonly PlayerService _playerService = new();
    private IPlayerService PlayerService => _playerService;
    
    [Parameter] public RenderFragment? ChildContent { get; set; }
    
    protected override void OnInitialized()
    {
        base.OnInitialized();
        
        // PlayerService is already created as a field, so it's immediately available to cascading components
        // It will be in uninitialized state until OnAfterRenderAsync when AudioPlaybackEngine is ready
    }
    
    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (firstRender)
        {
            // Initialize the PlayerService with the AudioPlaybackEngine now that it's available
            await _playerService.InitializeAsync(AudioPlaybackEngine);
            StateHasChanged();
        }
    }
}