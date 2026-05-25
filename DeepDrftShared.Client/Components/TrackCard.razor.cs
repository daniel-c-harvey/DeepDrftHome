using Microsoft.AspNetCore.Components;
using DeepDrftModels.DTOs;
using MudBlazor;

namespace DeepDrftShared.Client.Components;

public partial class TrackCard : ComponentBase
{
    [Parameter] public required TrackDto TrackModel { get; set; }
    [Parameter] public EventCallback<TrackDto> OnPlay { get; set; }
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
