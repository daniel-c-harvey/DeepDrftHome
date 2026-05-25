using DeepDrftContent.Data.Constants;
using DeepDrftData;
using DeepDrftModels.Entities;
using NetBlocks.Models;
using ContentTrackService = DeepDrftContent.Data.TrackService;
using FileDb = DeepDrftContent.Data.FileDatabase.Services.FileDatabase;

namespace DeepDrftContent.Services;

/// <summary>
/// Host-internal orchestrator that makes DeepDrftContent the single authority over both the
/// vault (FileDatabase) and SQL metadata (DeepDrftData). Owns the two-database write/delete
/// flow so the controller stays a thin HTTP boundary and no caller coordinates the two stores.
/// </summary>
public class UnifiedTrackService
{
    internal const string TrackNotFoundMessage = "Track not found.";
    private readonly ContentTrackService _contentTrackService;
    private readonly ITrackService _sqlTrackService;
    private readonly FileDb _fileDatabase;
    private readonly ILogger<UnifiedTrackService> _logger;

    public UnifiedTrackService(
        ContentTrackService contentTrackService,
        ITrackService sqlTrackService,
        FileDb fileDatabase,
        ILogger<UnifiedTrackService> logger)
    {
        _contentTrackService = contentTrackService;
        _sqlTrackService = sqlTrackService;
        _fileDatabase = fileDatabase;
        _logger = logger;
    }

    /// <summary>
    /// Process a WAV into the vault, then persist its metadata to SQL. On success the returned
    /// entity carries the SQL-assigned Id. If the vault write succeeds but the SQL persist fails,
    /// the audio is orphaned under EntryKey — logged loudly so it is recoverable manually.
    /// </summary>
    public async Task<ResultContainer<TrackEntity>> UploadAsync(
        string tempFilePath,
        string trackName,
        string artist,
        string? album,
        string? genre,
        DateOnly? releaseDate,
        long createdByUserId,
        CancellationToken ct)
    {
        var unpersisted = await _contentTrackService.AddTrackFromWavAsync(
            tempFilePath, trackName, artist, album, genre, releaseDate);

        if (unpersisted is null)
        {
            _logger.LogWarning("UploadAsync: content TrackService returned null for {TrackName}", trackName);
            return ResultContainer<TrackEntity>.CreateFailResult("Failed to process and store WAV.");
        }

        unpersisted.CreatedByUserId = createdByUserId;

        var saveResult = await _sqlTrackService.Create(unpersisted);
        if (!saveResult.Success || saveResult.Value is null)
        {
            // Vault write succeeded, SQL persist failed — audio is orphaned in the tracks vault
            // under EntryKey. Log loudly (include EntryKey) so it is recoverable manually.
            var error = saveResult.Messages.FirstOrDefault()?.Message ?? "Unknown error";
            _logger.LogError(
                "Track persisted to vault but SQL save failed. Orphaned entry: {EntryKey}. Error: {Error}",
                unpersisted.EntryKey, error);
            return ResultContainer<TrackEntity>.CreateFailResult($"Track was uploaded but could not be saved: {error}");
        }

        return saveResult;
    }

    /// <summary>
    /// Delete a track's SQL row, then its vault entry. SQL is the source of truth: a SQL delete
    /// failure fails the operation (and leaves the vault untouched), but a subsequent vault delete
    /// failure is logged as an orphan and swallowed — it is a maintenance concern, not user-facing.
    /// </summary>
    public async Task<Result> DeleteAsync(long id, CancellationToken ct)
    {
        var lookup = await _sqlTrackService.GetById(id);
        if (!lookup.Success)
        {
            var error = lookup.Messages.FirstOrDefault()?.Message ?? "unknown error";
            _logger.LogError("DeleteAsync: GetById failed for track {TrackId}: {Error}", id, error);
            return Result.CreateFailResult("Failed to load track.");
        }

        if (lookup.Value is null)
        {
            return Result.CreateFailResult(TrackNotFoundMessage);
        }

        var entryKey = lookup.Value.EntryKey;

        var sqlDelete = await _sqlTrackService.Delete(id);
        if (!sqlDelete.Success)
        {
            var error = sqlDelete.Messages.FirstOrDefault()?.Message ?? "unknown error";
            _logger.LogError("DeleteAsync: SQL delete failed for track {TrackId}: {Error}", id, error);
            return Result.CreateFailResult("Failed to delete track.");
        }

        // Tri-state per FileDatabase's error-swallow contract: null = vault missing/error,
        // false = entry not present, true = removed. Anything but a clean removal is an orphan.
        var removed = await _fileDatabase.RemoveResourceAsync(VaultConstants.Tracks, entryKey);
        if (removed is not true)
        {
            _logger.LogWarning(
                "Vault delete did not remove entry after SQL delete. {TrackId} {EntryKey} outcome={Outcome}",
                id, entryKey, removed);
        }

        return Result.CreatePassResult();
    }
}
