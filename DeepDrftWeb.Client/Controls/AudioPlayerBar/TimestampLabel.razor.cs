using Microsoft.AspNetCore.Components;

namespace DeepDrftWeb.Client.Controls.AudioPlayerBar;

public partial class TimestampLabel : ComponentBase
{
    [Parameter] public required double CurrentTime { get; set; }
    [Parameter] public required double? Duration { get; set; }
    private static string FormatTime(double seconds)
    {
        var timeSpan = TimeSpan.FromSeconds(seconds);
        return timeSpan.ToString(timeSpan.TotalHours >= 1 ? @"h\:mm\:ss" : @"m\:ss");
    }
}