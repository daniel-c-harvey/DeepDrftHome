using Microsoft.AspNetCore.Components;
using DeepDrftModels.Entities;
using DeepDrftWeb.Client.Clients;

namespace DeepDrftWeb.Client.Controls;

public partial class TracksGallery : ComponentBase
{
    [Parameter] public IEnumerable<TrackEntity> Tracks { get; set; } = [];
    [Parameter] public TrackEntity? SelectedTrack { get; set; }
    [Parameter] public EventCallback<TrackEntity?> SelectedTrackChanged { get; set; }
    
    private async Task HandlePlayClick(TrackEntity track)
    {
        if (SelectedTrack == track) return;
        SelectedTrack = track;
        StateHasChanged();

        if (SelectedTrackChanged.HasDelegate)
        {
            await SelectedTrackChanged.InvokeAsync(track);
        }
    }
}