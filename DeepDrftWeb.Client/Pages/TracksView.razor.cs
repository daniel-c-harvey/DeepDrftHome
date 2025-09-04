using DeepDrftModels.Entities;
using DeepDrftModels.Models;
using DeepDrftWeb.Client.ViewModels;
using Microsoft.AspNetCore.Components;

namespace DeepDrftWeb.Client.Pages;

public partial class TracksView : ComponentBase
{
    [Inject]
    public required TracksViewModel ViewModel { get; set; }


    protected override async Task OnInitializedAsync()
    {
        await SetPage(1);
    }
    
    private async Task SetPage(int newPage)
    {
        var result = await ViewModel.TrackClient.GetPage(newPage, ViewModel.PageSize, ViewModel.SortBy, ViewModel.IsDescending);

        if (result is { Success: true, Value: PagedResult<TrackEntity> pageResult })
        {
            ViewModel.Page = pageResult;
            ViewModel.PageSize = pageResult.PageSize;
        }
    }
}