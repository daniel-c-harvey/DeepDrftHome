using DeepDrftModels.DTOs;
using DeepDrftPublic.Client.Services;
using DeepDrftPublic.Client.ViewModels;
using Microsoft.AspNetCore.Components;
using Models.Common;

namespace DeepDrftPublic.Client.Pages;

public partial class TracksView : ComponentBase
{
    [Inject] public required TracksViewModel ViewModel { get; set; }
    [CascadingParameter] public required IPlayerService PlayerService { get; set; }
    
    private TrackDto? _selectedTrack = null;
    private int _clickCount = 0;
    private string _lifecycleStatus = "Not initialized";
    
    protected override async Task OnInitializedAsync()
    {
        _lifecycleStatus = "OnInitializedAsync called";
        await SetPage(1);
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (firstRender)
        {
            _lifecycleStatus = "OnAfterRenderAsync called - WebAssembly is active!";
            StateHasChanged();
        }
    }

    private void TestInteractivity()
    {
        _clickCount++;
        _lifecycleStatus = $"Button clicked {_clickCount} times - Interactivity working!";
    }
    
    private async Task SetPage(int newPage)
    {
        var result = await ViewModel.TrackData.GetPage(newPage, ViewModel.PageSize, ViewModel.SortBy, ViewModel.IsDescending);

        if (result is { Success: true, Value: PagedResult<TrackDto> pageResult })
        {
            ViewModel.Page = pageResult;
            ViewModel.PageSize = pageResult.PageSize;
        }
    }

    private async Task PlayTrack(TrackDto? track)
    {
        if (track == null && _selectedTrack == null || track?.Id == _selectedTrack?.Id) return;

        if (track is null)
        {
            await PlayerService.Unload();
        }
        else
        {
            await PlayerService.SelectTrack(track);
        }
        
        _selectedTrack = track;
    }
}