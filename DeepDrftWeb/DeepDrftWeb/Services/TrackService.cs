using DeepDrftModels.Entities;
using DeepDrftModels.Models;
using DeepDrftWeb.Data;
using DeepDrftWeb.Data.Repositories;
using NetBlocks.Models;

namespace DeepDrftWeb.Services;

public class TrackService
{
    private readonly string _sortLastAscending = Enumerable.Repeat(char.MaxValue, 64).Aggregate(string.Empty, (a, b) => a + b);
    private readonly string _sortLastDescending = Enumerable.Repeat(char.MinValue.ToString(), 64).Aggregate(string.Empty, (a, b) => a + b);
    private readonly TrackRepository _repository;

    public TrackService(TrackRepository repository)
    {
        _repository = repository;
    }

    public async Task<ResultContainer<TrackEntity?>> GetById(long id)
    {
        try
        {
            var track = await _repository.GetById(id);
            return ResultContainer<TrackEntity?>.CreatePassResult(track);
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
            var tracks = await _repository.GetAll();
            return ResultContainer<List<TrackEntity>>.CreatePassResult(tracks);
        }
        catch (Exception e)
        {
            return  ResultContainer<List<TrackEntity>>.CreateFailResult(e.Message);
        }
    }

    public async Task<ResultContainer<PagedResult<TrackEntity>>> GetPaged(int pageNumber, int pageSize, string? sortColumn, bool sortDescending)
    {
        try
        {
            var parameters = new PagingParameters<TrackEntity>()
            {
                Page = pageNumber,
                PageSize = pageSize,
                IsDescending = sortDescending
            };

            if (sortColumn != null)
            {
                switch (sortColumn)
                {
                    case "TrackName":
                        parameters.OrderBy = entity => entity.TrackName;
                        break;
                    case "Artist":
                        parameters.OrderBy = entity => entity.Artist;
                        break;
                    case "Album":
                        parameters.OrderBy = entity => entity.Album ?? _sortLastAscending;
                        break;
                    case "ReleaseDate":
                        parameters.OrderBy = entity => entity.ReleaseDate ?? DateOnly.MaxValue;
                        break;
                    case "Genre":
                        parameters.OrderBy = entity => entity.Genre ?? _sortLastAscending;
                        break;
                    
                }
            }
            
            var page = await _repository.GetPage(parameters);
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
            var track = await _repository.Create(newTrack);
            return ResultContainer<TrackEntity>.CreatePassResult(track);
        }
        catch (Exception e)
        {
            return ResultContainer<TrackEntity>.CreateFailResult(e.Message);
        }
    }

    public async Task<ResultContainer<TrackEntity>> Update(TrackEntity track)
    {
        try
        {
            var updatedTrack = await _repository.Update(track);
            return ResultContainer<TrackEntity>.CreatePassResult(updatedTrack);
        }
        catch (Exception e)
        {
            return ResultContainer<TrackEntity>.CreateFailResult(e.Message);
        }
    }

    public async Task<Result> Delete(long id)
    {
        try
        {
            await  _repository.Delete(id);
            return Result.CreatePassResult();
        }
        catch (Exception e)
        {
            return Result.CreateFailResult(e.Message);
        }
    }
}