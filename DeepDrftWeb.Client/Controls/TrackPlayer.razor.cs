using Microsoft.AspNetCore.Components;
using DeepDrftModels.Entities;
using DeepDrftWeb.Client.Clients;
using MudBlazor;

namespace DeepDrftWeb.Client.Controls;

public partial class TrackPlayer : ComponentBase
{
    [Parameter] public required TrackEntity Track { get; set; }
    [Inject] public required TrackMediaClient Client { get; set; }
    
    private Stream? _audioStream = null;
    private bool _isPlaying = false;
    private string _playPauseIcon => _isPlaying ? Icons.Material.Filled.Pause : Icons.Material.Filled.PlayArrow;
    private async Task HandlePlayClick()
    {
        if (_audioStream == null)
        {
            _audioStream = await Client.GetTrackMedia(Track.EntryKey);
            PlayAudio();
        }
    }

    private void PlayAudio()
    {
        throw new NotImplementedException();
    }
}