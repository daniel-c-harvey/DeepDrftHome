using Microsoft.AspNetCore.Components;
using MudBlazor;

namespace DeepDrftWeb.Client.Controls.AudioPlayerBar;

public partial class VolumeControls : ComponentBase
{
    [Parameter] public required double Volume { get; set; }
    [Parameter] public required EventCallback<double> VolumeChanged { get; set; }
    private string GetVolumeIcon()
    {
        if (Volume == 0) return Icons.Material.Filled.VolumeOff;
        if (Volume < 0.5) return Icons.Material.Filled.VolumeDown;
        return Icons.Material.Filled.VolumeUp;
    }
}