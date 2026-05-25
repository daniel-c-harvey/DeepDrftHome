using DeepDrftModels.DTOs;
using DeepDrftPublic.Client.Services;
using Models.Common;

namespace DeepDrftPublic.Client.ViewModels;

public class TracksViewModel
{
    public ITrackDataService TrackData { get; }

    public int PageNumber { get; set; } = 1;

    public int PageSize
    {
        get => Page?.PageSize ?? 15;
        set
        {
            if (Page == null) return;
            if (value != Page.PageSize)
            {
                Page.PageSize = value;
            }
        }
    }
    public string SortBy { get; set; } = string.Empty;
    public bool IsDescending { get; set; } = false;
    public PagedResult<TrackDto>? Page { get; set; } = null;

    public TracksViewModel(ITrackDataService trackData)
    {
        TrackData = trackData;
    }
}
