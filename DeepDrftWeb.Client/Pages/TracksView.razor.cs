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
            await AudioPlaybackEngine.InitializeAudioPlayer();
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
        var result = await ViewModel.TrackClient.GetPage(newPage, ViewModel.PageSize, ViewModel.SortBy, ViewModel.IsDescending);

        if (result is { Success: true, Value: PagedResult<TrackEntity> pageResult })
        {
            ViewModel.Page = pageResult;
            ViewModel.PageSize = pageResult.PageSize;
        }
    }

    private async Task PlayTrack(TrackEntity? track)
    {
        if (track == null && _selectedTrack == null || track?.Id == _selectedTrack?.Id) return;
        
        await AudioPlaybackEngine.LoadTrack(track);
        StateHasChanged();
    }
}