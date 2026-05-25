using Data.Managers;
using DeepDrftData.Repositories;
using DeepDrftModels.DTOs;
using DeepDrftModels.Entities;
using Microsoft.Extensions.Logging;
using Models.Common;
using NetBlocks.Models;

namespace DeepDrftData;

/// <summary>
/// SQL-side track service built on the BlazorBlocks Manager base. The layer boundary:
/// TrackRepository outputs entities; this service outputs DTOs via TrackConverter — the
/// single authoritative entity↔DTO conversion path. The ITrackService surface is DTO-typed
/// throughout; the entity never escapes the service layer.
///
/// The base Manager&lt;&gt; surface does not line up with ITrackService by signature (base
/// Add vs Create, base Update→Result vs Update→DTO, base Get/GetPage vs GetAll/GetPaged,
/// base GetById→TDto vs GetById→TDto?), so the query and mutation methods are implemented
/// here over Repository + TrackConverter. Only Delete(long)→Result is inherited unchanged.
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

    // Explicit impl: base GetById returns ResultContainer<TrackDto> (fails on miss); the
    // service contract is ResultContainer<TrackDto?> (pass with null on miss). Return types
    // differ, so this cannot be a public overload of the inherited member.
    async Task<ResultContainer<TrackDto?>> ITrackService.GetById(long id)
    {
        try
        {
            var entity = await Repository.GetByIdAsync(id);
            return ResultContainer<TrackDto?>.CreatePassResult(
                entity is null ? null : TrackConverter.Convert(entity));
        }
        catch (Exception e)
        {
            return ResultContainer<TrackDto?>.CreateFailResult(e.Message);
        }
    }

    public async Task<ResultContainer<List<TrackDto>>> GetAll()
    {
        try
        {
            var entities = await Repository.GetAllAsync();
            return ResultContainer<List<TrackDto>>.CreatePassResult(
                entities.Select(TrackConverter.Convert).ToList());
        }
        catch (Exception e)
        {
            return ResultContainer<List<TrackDto>>.CreateFailResult(e.Message);
        }
    }

    public async Task<ResultContainer<PagedResult<TrackDto>>> GetPaged(
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
            var dtoPage = PagedResult<TrackDto>.From(page, page.Items.Select(TrackConverter.Convert));
            return ResultContainer<PagedResult<TrackDto>>.CreatePassResult(dtoPage);
        }
        catch (Exception e)
        {
            return ResultContainer<PagedResult<TrackDto>>.CreateFailResult(e.Message);
        }
    }

    public async Task<ResultContainer<TrackDto>> Create(TrackDto newTrack)
    {
        try
        {
            var added = await Repository.AddAsync(TrackConverter.Convert(newTrack));
            return ResultContainer<TrackDto>.CreatePassResult(TrackConverter.Convert(added));
        }
        catch (Exception e)
        {
            return ResultContainer<TrackDto>.CreateFailResult(e.Message);
        }
    }

    // Explicit impl: base Update returns Result; the service contract returns the persisted
    // DTO so the CMS edit flow reads back DB-authoritative values.
    async Task<ResultContainer<TrackDto>> ITrackService.Update(TrackDto track)
    {
        try
        {
            await Repository.UpdateAsync(TrackConverter.Convert(track));
            var updated = await Repository.GetByIdAsync(track.Id);
            return updated is not null
                ? ResultContainer<TrackDto>.CreatePassResult(TrackConverter.Convert(updated))
                : ResultContainer<TrackDto>.CreateFailResult("Track not found after update.");
        }
        catch (Exception e)
        {
            return ResultContainer<TrackDto>.CreateFailResult(e.Message);
        }
    }

    // Delete(long) → Result is inherited from Manager<> and satisfies ITrackService.Delete
    // by signature. No override.
}
