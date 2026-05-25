using DeepDrftModels.DTOs;
using Models.Common;
using NetBlocks.Models;

namespace DeepDrftData;

/// <summary>
/// SQL-side track service. Repository outputs entities; this service outputs DTOs via
/// TrackConverter. In-process consumers (UnifiedTrackService, CLI, DeepDrftPublic) all
/// receive DTOs.
/// </summary>
public interface ITrackService
{
    Task<ResultContainer<TrackDto?>> GetById(long id);
    Task<ResultContainer<List<TrackDto>>> GetAll();
    Task<ResultContainer<PagedResult<TrackDto>>> GetPaged(int pageNumber, int pageSize, string? sortColumn, bool sortDescending, CancellationToken cancellationToken = default);
    Task<ResultContainer<TrackDto>> Create(TrackDto newTrack);
    Task<ResultContainer<TrackDto>> Update(TrackDto track);
    Task<Result> Delete(long id);
}
