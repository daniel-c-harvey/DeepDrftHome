using DeepDrftData;
using DeepDrftModels.Entities;
using DeepDrftPublic.Client.Services;
using Models.Common;
using NetBlocks.Models;

namespace DeepDrftPublic.Services;

/// <summary>
/// Server-side <see cref="ITrackDataService"/> that calls <see cref="ITrackService"/>
/// in-process (EF Core / SQL). Replaces the loopback HTTP hop during SSR prerender:
/// the WASM interactive pass still uses <see cref="Client.Services.TrackClientDataService"/>
/// over HTTP, but on the server we already have the domain service in DI.
/// </summary>
public class TrackDirectDataService : ITrackDataService
{
    private readonly ITrackService _trackService;

    public TrackDirectDataService(ITrackService trackService)
    {
        _trackService = trackService;
    }

    public async Task<ApiResult<PagedResult<TrackEntity>>> GetPage(
        int pageNumber,
        int pageSize,
        string? sortColumn = null,
        bool sortDescending = false)
    {
        var result = await _trackService.GetPaged(pageNumber, pageSize, sortColumn, sortDescending);
        return ApiResult<PagedResult<TrackEntity>>.From(result);
    }
}
