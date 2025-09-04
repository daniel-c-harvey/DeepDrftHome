using Microsoft.AspNetCore.Components;
using DeepDrftModels.Entities;

namespace DeepDrftWeb.Client.Controls;

public partial class TrackPlayer : ComponentBase
{
    [Parameter] public TrackEntity? Track { get; set; }

    private void HandlePlayClick()
    {
        // TODO: Implement play functionality with injected service
    }
}