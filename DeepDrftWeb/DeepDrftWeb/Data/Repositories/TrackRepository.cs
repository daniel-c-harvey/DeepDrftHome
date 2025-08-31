using DeepDrftModels.Entities;
using DeepDrftModels.Models;
using Microsoft.EntityFrameworkCore;

namespace DeepDrftWeb.Data.Repositories;

public class TrackRepository
{
    private readonly DeepDrftContext _db;
    
    public TrackRepository(DeepDrftContext db)
    {
        _db = db;
    }

    public async Task<TrackEntity?> GetById(long id)
    {
        return await _db.Tracks.FindAsync(id);
    }

    public async Task<List<TrackEntity>> GetAll()
    {
        return await _db.Tracks.ToListAsync();
    }

    public async Task<PagedResult<TrackEntity>> GetPage(PagingParameters<TrackEntity> pageParameters)
    {
        var count = await _db.Tracks.CountAsync();
        
        var page = await _db.Tracks
            .OrderBy(pageParameters.OrderBy ?? (t => t.Id))
            .Skip((pageParameters.Page - 1) * pageParameters.PageSize)
            .Take(pageParameters.PageSize)
            .ToListAsync();
        
        return new PagedResult<TrackEntity>(page, count, pageParameters.Page, pageParameters.PageSize);
    }

    public async Task<TrackEntity> Create(TrackEntity newTrack)
    {
        var track = _db.Tracks.Add(newTrack);
        await _db.SaveChangesAsync();
        return track.Entity;
    }

    public async Task<TrackEntity> Update(TrackEntity track)
    {
        var trackEntity = await GetById(track.Id);

        if (trackEntity == null)
        {
            return await Create(track);
        }
        
        trackEntity.Album = track.Album;
        trackEntity.Artist = track.Artist;
        trackEntity.Genre = track.Genre;
        trackEntity.ImagePath = track.ImagePath;
        trackEntity.MediaPath = track.MediaPath;
        trackEntity.ReleaseDate = track.ReleaseDate;
        trackEntity.TrackName = track.TrackName;
        
        await _db.SaveChangesAsync();
        return trackEntity;
    }

    public async Task Delete(long id)
    {
        var track = await GetById(id);
        if (track != null)
        {
            _db.Tracks.Remove(track);
            await _db.SaveChangesAsync();
        }
    }
}