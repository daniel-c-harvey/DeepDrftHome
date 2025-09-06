using Microsoft.AspNetCore.Components;
using DeepDrftModels.Entities;
using DeepDrftWeb.Client.Clients;

namespace DeepDrftWeb.Client.Controls;

public partial class TracksGallery : ComponentBase
{
    private Stream? _audioStream = null;
    [Parameter] public IEnumerable<TrackEntity> Tracks { get; set; } = Enumerable.Empty<TrackEntity>();
    
    [Inject] public required TrackMediaClient Client { get; set; }
    
    private async Task HandlePlayClick(TrackEntity track)
    {
        if (_audioStream == null)
        {
            _audioStream = await Client.GetTrackMedia(track.EntryKey);
            PlayAudio();
        }
    }

    private void PlayAudio()
    {
        throw new NotImplementedException();
    }
    
}