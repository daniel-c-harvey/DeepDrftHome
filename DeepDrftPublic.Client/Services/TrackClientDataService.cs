using DeepDrftModels.DTOs;
using DeepDrftPublic.Client.Clients;
using Models.Common;
using NetBlocks.Models;

namespace DeepDrftPublic.Client.Services;

/// <summary>
/// WASM-side <see cref="ITrackDataService"/> that delegates to <see cref="TrackClient"/>
/// (HTTP to the <c>DeepDrft.API</c> backend). Used on the WASM interactive render pass;
/// the server prerender pass swaps in a direct, in-process implementation.
/// </summary>
public class TrackClientDataService : ITrackDataService
{
    private readonly TrackClient _trackClient;

    public TrackClientDataService(TrackClient trackClient)
    {
        _trackClient = trackClient;
    }

    public Task<ApiResult<PagedResult<TrackDto>>> GetPage(
        int pageNumber,
        int pageSize,
        string? sortColumn = null,
        bool sortDescending = false)
        => _trackClient.GetPage(pageNumber, pageSize, sortColumn, sortDescending);
}
