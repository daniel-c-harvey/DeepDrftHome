using Microsoft.AspNetCore.Components;
using DeepDrftModels.DTOs;

namespace DeepDrftShared.Client.Components;

public partial class TracksGallery : ComponentBase
{
    [Parameter] public IEnumerable<TrackDto> Tracks { get; set; } = [];
    [Parameter] public TrackDto? SelectedTrack { get; set; }
    [Parameter] public EventCallback<TrackDto?> SelectedTrackChanged { get; set; }

    private async Task HandlePlayClick(TrackDto track)
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
