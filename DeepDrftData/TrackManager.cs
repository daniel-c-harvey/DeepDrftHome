using Data.Managers;
using DeepDrftData.Repositories;
using DeepDrftModels.DTOs;
using DeepDrftModels.Entities;
using Microsoft.Extensions.Logging;
using Models.Common;
using NetBlocks.Models;

namespace DeepDrftData;

/// <summary>
/// SQL-side track orchestrator built on the BlazorBlocks Manager base. Two surfaces coexist:
///
///   - The DTO-shaped IManager surface (GetById → TrackDto, etc.) inherited from Manager&lt;&gt;.
///   - The entity-shaped ITrackService surface retained for backward compatibility with the
///     web host, CMS, and CLI — all existing controllers and pages inject ITrackService and
///     expect TrackEntity-typed results. The two GetById overloads conflict on signature, so
///     ITrackService.GetById is implemented explicitly; the base Manager.Delete satisfies
///     both interfaces because the signatures align.
/// </summary>
public class TrackManager
    : Manager<TrackEntity, TrackDto, TrackRepository, TrackConverter>, ITrackService
{
    public TrackManager(
        TrackRepository repository,
        ILogger<Manager<TrackEntity, TrackDto, TrackRepository, TrackConverter>> logger)
        : base(repository, logger)
    {
    }

    // --- ITrackService implementation (entity-space; calls Repository directly) ---

    // Explicit interface implementation — IManager.GetById returns ResultContainer<TrackDto>
    // (inherited from Manager<>), so this entity-typed overload cannot coexist as a public
    // member with the same name. Callers always inject ITrackService, so the explicit impl
    // resolves correctly at the call site.
    async Task<ResultContainer<TrackEntity?>> ITrackService.GetById(long id)
    {
        try
        {
            var entity = await Repository.GetByIdAsync(id);
            return ResultContainer<TrackEntity?>.CreatePassResult(entity);
        }
        catch (Exception e)
        {
            return ResultContainer<TrackEntity?>.CreateFailResult(e.Message);
        }
    }

    public async Task<ResultContainer<List<TrackEntity>>> GetAll()
    {
        try
        {
            var entities = await Repository.GetAllAsync();
            return ResultContainer<List<TrackEntity>>.CreatePassResult(entities.ToList());
        }
        catch (Exception e)
        {
            return ResultContainer<List<TrackEntity>>.CreateFailResult(e.Message);
        }
    }

    public async Task<ResultContainer<PagedResult<TrackEntity>>> GetPaged(
        int pageNumber,
        int pageSize,
        string? sortColumn,
        bool sortDescending,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var parameters = new PagingParameters<TrackEntity>
            {
                Page = pageNumber,
                PageSize = pageSize,
                IsDescending = sortDescending,
                OrderBy = sortColumn switch
                {
                    "TrackName"   => e => e.TrackName,
                    "Artist"      => e => e.Artist,
                    "Album"       => e => (object)(e.Album ?? string.Empty),
                    "Genre"       => e => (object)(e.Genre ?? string.Empty),
                    "ReleaseDate" => e => (object)(e.ReleaseDate ?? DateOnly.MaxValue),
                    _             => e => e.Id
                }
            };

            var page = await Repository.GetPagedAsync(parameters);
            return ResultContainer<PagedResult<TrackEntity>>.CreatePassResult(page);
        }
        catch (Exception e)
        {
            return ResultContainer<PagedResult<TrackEntity>>.CreateFailResult(e.Message);
        }
    }

    public async Task<ResultContainer<TrackEntity>> Create(TrackEntity newTrack)
    {
        try
        {
            var added = await Repository.AddAsync(newTrack);
            return ResultContainer<TrackEntity>.CreatePassResult(added);
        }
        catch (Exception e)
        {
            return ResultContainer<TrackEntity>.CreateFailResult(e.Message);
        }
    }

    // Manager<>.Update takes TrackDto and returns Result; this Update keeps the
    // entity-typed contract callers expect and returns the post-update entity for the
    // existing CMS edit flow that reads back the persisted values.
    /// <summary>
    /// Updates the track's metadata fields and returns the DB-authoritative entity.
    /// The caller's <paramref name="track"/> object has its <c>UpdatedAt</c> field
    /// mutated in place by <see cref="TrackRepository.UpdateAsync"/>; do not reuse it.
    /// </summary>
    public async Task<ResultContainer<TrackEntity>> Update(TrackEntity track)
    {
        try
        {
            await Repository.UpdateAsync(track);
            var updated = await Repository.GetByIdAsync(track.Id);
            return updated is not null
                ? ResultContainer<TrackEntity>.CreatePassResult(updated)
                : ResultContainer<TrackEntity>.CreateFailResult("Track not found after update.");
        }
        catch (Exception e)
        {
            return ResultContainer<TrackEntity>.CreateFailResult(e.Message);
        }
    }

    // Delete(long) is inherited from Manager<> — its Task<Result> signature already
    // satisfies ITrackService.Delete, and the base implementation performs the soft delete
    // via Repository.DeleteAsync. No override needed.
}
