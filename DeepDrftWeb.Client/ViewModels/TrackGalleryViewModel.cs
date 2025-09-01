using DeepDrftModels.Entities;
using DeepDrftModels.Models;
using DeepDrftWeb.Client.Clients;

namespace DeepDrftWeb.Client.ViewModels;

public class TrackGalleryViewModel
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
    
    public TrackGalleryViewModel(TrackClient trackClient)
    {
        TrackClient = trackClient;
    }
}