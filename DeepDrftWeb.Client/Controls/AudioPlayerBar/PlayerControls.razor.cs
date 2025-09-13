using Microsoft.AspNetCore.Components;
using MudBlazor;

namespace DeepDrftWeb.Client.Controls.AudioPlayerBar;

public partial class PlayerControls : ComponentBase
{
    [Parameter] public required bool IsPlaying { get; set; }
    [Parameter] public required bool IsLoaded { get; set; }
    [Parameter] public required EventCallback TogglePlayPause { get; set; }
    [Parameter] public required EventCallback Stop { get; set; }
    private string GetPlayIcon()
    {
        return IsPlaying ? Icons.Material.Filled.Pause : Icons.Material.Filled.PlayArrow;
    }
}