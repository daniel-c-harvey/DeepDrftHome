using Microsoft.AspNetCore.Components;
using DeepDrftModels.Entities;
using DeepDrftWeb.Client.Clients;
using MudBlazor;

namespace DeepDrftWeb.Client.Controls;

public partial class TrackCard : ComponentBase
{
    [Parameter] public required TrackEntity TrackModel { get; set; }
    [Parameter] public EventCallback<TrackEntity> OnPlay { get; set; }
    
    private bool _isPlaying = false;
    private string PlayPauseIcon => _isPlaying ? Icons.Material.Filled.MusicNote : Icons.Material.Filled.PlayArrow;

    private async Task PlayClick()
    {
        if (!_isPlaying)
        {
            _isPlaying = true;
            await OnPlay.InvokeAsync();
        }
    }
}