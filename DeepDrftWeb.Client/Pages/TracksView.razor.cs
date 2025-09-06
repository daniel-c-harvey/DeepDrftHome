using DeepDrftModels.Entities;
using DeepDrftModels.Models;
using DeepDrftWeb.Client.Services;
using DeepDrftWeb.Client.ViewModels;
using Microsoft.AspNetCore.Components;

namespace DeepDrftWeb.Client.Pages;

public partial class TracksView : ComponentBase
{
    [Inject] public required TracksViewModel ViewModel { get; set; }
    [Inject] public required AudioPlaybackEngine AudioPlaybackEngine { get; set; }

    private TrackEntity? _selectedTrack = null;
    
    protected override async Task OnInitializedAsync()
    {
        await SetPage(1);
        
        if (!RendererInfo.IsInteractive) return;
        await AudioPlaybackEngine.InitializeAudioPlayer();
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

    private async Task PlayTrack(TrackEntity? track)
    {
        if (track == null) return;
        
        await AudioPlaybackEngine.LoadTrack(track);
        StateHasChanged();
    }
}