using Microsoft.AspNetCore.Components;
using DeepDrftModels.Entities;

namespace DeepDrftWeb.Client.Controls;

public partial class TracksGallery : ComponentBase
{
    [Parameter] public IEnumerable<TrackEntity> Tracks { get; set; } = Enumerable.Empty<TrackEntity>();
}