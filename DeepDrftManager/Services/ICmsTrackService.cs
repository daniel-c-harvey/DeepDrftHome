using DeepDrftModels.Entities;
using NetBlocks.Models;

namespace DeepDrftManager.Services;

/// <summary>
/// CMS-side track mutations for the Manager host. Coordinates the dual-database write:
/// SQL metadata via <c>ITrackService</c> and binary audio in DeepDrftContent's vault over HTTP.
/// DeepDrftManager intentionally does not reference DeepDrftContent.Services (CMS-PLAN §5,
/// Option B) — all vault access is over the network to DeepDrftContent.
/// </summary>
public interface ICmsTrackService
{
    /// <summary>
    /// Proxy a WAV upload to DeepDrftContent, then persist the returned metadata to SQL.
    /// On success the returned entity carries the SQL-assigned <c>Id</c>. If the vault write
    /// succeeds but the SQL persist fails, the audio is orphaned under <c>EntryKey</c> — the
    /// failure is logged loudly and surfaced as a failed result.
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
    /// Delete a track's SQL row, then its vault entry. SQL is the source of truth: a SQL
    /// delete failure fails the operation, but a subsequent vault delete failure is logged
    /// and swallowed (the orphan is a maintenance concern, not a user-facing error).
    /// </summary>
    Task<Result> DeleteTrackAsync(long id, CancellationToken ct = default);
}
