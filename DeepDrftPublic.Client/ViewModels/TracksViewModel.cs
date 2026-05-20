using DeepDrftModels.Entities;
using DeepDrftPublic.Client.Clients;
using Models.Common;

namespace DeepDrftPublic.Client.ViewModels;

public class TracksViewModel
{
    public TrackClient TrackClient { get; }

    // private int _pageNumber = 1;
    public int PageNumber { get; set; } = 1;

    public int PageSize
    {
        get => Page?.PageSize ?? 15;
        set
        {
            if (Page == null) throw new Exception();
            if (value != Page.PageSize)
            {
                Page.PageSize = value;
            }
        }
    }
    public string SortBy { get; set; } = string.Empty;
    public bool IsDescending { get; set; } = false;
    public PagedResult<TrackEntity>? Page { get; set; } = null;
    
    public TracksViewModel(TrackClient trackClient)
    {
        TrackClient = trackClient;
    }
}