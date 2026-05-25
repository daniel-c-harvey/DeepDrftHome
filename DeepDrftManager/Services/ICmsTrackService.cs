using DeepDrftModels.Entities;
using Models.Common;
using NetBlocks.Models;

namespace DeepDrftManager.Services;

/// <summary>
/// CMS-side track operations for the Manager host. Every read and write goes over HTTP to the
/// DeepDrftAPI API, which is the single authority over both the SQL metadata store and the
/// binary audio vault. DeepDrftManager holds no in-process data layer.
/// </summary>
public interface ICmsTrackService
{
    /// <summary>
    /// Proxy a WAV upload to DeepDrftAPI. The Content API owns the dual-database write and
    /// returns the persisted entity carrying the SQL-assigned <c>Id</c>. A vault-without-SQL
    /// orphan is handled and logged server-side; here it surfaces as a failed result.
    /// </summary>
    Task<ResultContainer<TrackEntity>> UploadTrackAsync(
        Stream wavStream,
        string fileName,
        string contentType,
        string trackName,
        string artist,
        string? album,
        string? genre,
        string? releaseDate,
        long createdByUserId,
        CancellationToken ct = default);

    /// <summary>
    /// Delete a track via the Content API, which removes the SQL row then the vault entry.
    /// Maps a 404 to a "Track not found." failure.
    /// </summary>
    Task<Result> DeleteTrackAsync(long id, CancellationToken ct = default);

    /// <summary>
    /// Fetch a page of track metadata from the Content API's <c>GET api/track/page</c>.
    /// </summary>
    Task<ResultContainer<PagedResult<TrackEntity>>> GetPagedAsync(
        int page, int pageSize, string? sortColumn, bool sortDescending,
        CancellationToken ct = default);

    /// <summary>
    /// Fetch a single track's metadata from <c>GET api/track/meta/{id}</c>. A 404 returns a
    /// passing result with a null value.
    /// </summary>
    Task<ResultContainer<TrackEntity?>> GetByIdAsync(long id, CancellationToken ct = default);

    /// <summary>
    /// Update a track's metadata via <c>PUT api/track/meta/{id}</c>. EntryKey is immutable and
    /// not part of the update.
    /// </summary>
    Task<Result> UpdateAsync(
        long id, string trackName, string artist,
        string? album, string? genre, DateOnly? releaseDate,
        CancellationToken ct = default);
}
