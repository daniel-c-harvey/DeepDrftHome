using DeepDrftModels.DTOs;
using Models.Common;
using NetBlocks.Models;

namespace DeepDrftPublic.Client.Services;

/// <summary>
/// Track metadata fetch abstraction with two render-mode-specific implementations:
///
///   - Server prerender pass: <c>TrackDirectDataService</c> in the DeepDrftPublic host
///     resolves <see cref="DeepDrftData.ITrackService"/> in-process (EF Core / SQL) and
///     avoids a loopback HTTP hop.
///   - WASM interactive pass: <c>TrackClientDataService</c> in this assembly delegates
///     to <see cref="Clients.TrackClient"/> over HTTP.
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
