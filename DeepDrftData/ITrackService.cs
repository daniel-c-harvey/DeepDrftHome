using DeepDrftModels.Entities;
using Models.Common;
using NetBlocks.Models;

namespace DeepDrftData;

public interface ITrackService
{
    Task<ResultContainer<TrackEntity?>> GetById(long id);
    Task<ResultContainer<List<TrackEntity>>> GetAll();
    Task<ResultContainer<PagedResult<TrackEntity>>> GetPaged(int pageNumber, int pageSize, string? sortColumn, bool sortDescending, CancellationToken cancellationToken = default);
    Task<ResultContainer<TrackEntity>> Create(TrackEntity newTrack);
    Task<ResultContainer<TrackEntity>> Update(TrackEntity track);
    Task<Result> Delete(long id);
}
