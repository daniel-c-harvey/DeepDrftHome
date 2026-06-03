using DeepDrftModels.DTOs;
using DeepDrftPublic.Client.Services;
using DeepDrftPublic.Client.ViewModels;
using Microsoft.AspNetCore.Components;
using Models.Common;

namespace DeepDrftPublic.Client.Pages;

public partial class TracksView : ComponentBase, IDisposable
{
    [Inject] public required TracksViewModel ViewModel { get; set; }
    [CascadingParameter] public required IStreamingPlayerService PlayerService { get; set; }

    private TrackDto? _selectedTrack = null;
    private int _clickCount = 0;
    private string _lifecycleStatus = "Not initialized";
    private IStreamingPlayerService? _subscribedService;

    protected override async Task OnInitializedAsync()
    {
        _lifecycleStatus = "OnInitializedAsync called";
        await SetPage(1);
    }

    protected override void OnParametersSet()
    {
        // The Stop/Close buttons on the player bar reset the player directly,
        // bypassing PlayTrack — so the gallery's selection must follow player
        // state rather than only its own clicks. Subscribe to the multicast
        // side-channel (the cascade is IsFixed, so provider re-renders don't
        // reach us) and clear the highlight when nothing is loaded.
        if (PlayerService != null && !ReferenceEquals(PlayerService, _subscribedService))
        {
            if (_subscribedService != null)
                _subscribedService.StateChanged -= OnPlayerStateChanged;

            PlayerService.StateChanged += OnPlayerStateChanged;
            _subscribedService = PlayerService;
        }
    }

    private void OnPlayerStateChanged()
    {
        // Sync the gallery selection to the player. When the player is no longer
        // loaded (stopped/closed/ended) drop the highlight; guard against a
        // redundant re-render when nothing actually changed.
        if (!PlayerService.IsLoaded && _selectedTrack != null)
        {
            _selectedTrack = null;
            InvokeAsync(StateHasChanged);
        }
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

    public void Dispose()
    {
        if (_subscribedService != null)
        {
            _subscribedService.StateChanged -= OnPlayerStateChanged;
            _subscribedService = null;
        }
    }
}