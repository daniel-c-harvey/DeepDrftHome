using DeepDrftModels.DTOs;
using Models.Common;
using NetBlocks.Models;

namespace DeepDrftPublic.Client.Services;

/// <summary>
/// Track metadata fetch abstraction. Both SSR and WASM passes are served by
/// <c>TrackClientDataService</c> in this assembly, which delegates to
/// <see cref="Clients.TrackClient"/> over HTTP.
///
/// Components inject this single seam so they do not branch on render mode.
/// </summary>
public interface ITrackDataService
{
    Task<ApiResult<PagedResult<TrackDto>>> GetPage(
        int pageNumber,
        int pageSize,
        string? sortColumn = null,
        bool sortDescending = false);
}
