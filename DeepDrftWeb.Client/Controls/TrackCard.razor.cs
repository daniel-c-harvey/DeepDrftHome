using Microsoft.AspNetCore.Components;
using DeepDrftModels.Entities;
using DeepDrftWeb.Client.Clients;
using MudBlazor;

namespace DeepDrftWeb.Client.Controls;

public partial class TrackCard : ComponentBase
{
    [Parameter] public required TrackEntity TrackModel { get; set; }
    [Parameter] public EventCallback<TrackEntity> OnPlay { get; set; }
    [Parameter] public bool IsPlaying { get; set; } = false;
    
    private string PlayPauseIcon => IsPlaying ? Icons.Material.Filled.MusicNote : Icons.Material.Filled.PlayArrow;

    private async Task PlayClick()
    {
        if (!IsPlaying && OnPlay.HasDelegate)
        {
            await OnPlay.InvokeAsync(TrackModel);
        }
    }
}