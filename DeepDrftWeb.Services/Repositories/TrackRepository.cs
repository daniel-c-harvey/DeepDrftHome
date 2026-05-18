using DeepDrftModels.Entities;
using DeepDrftModels.Models;
using Microsoft.EntityFrameworkCore;
using DeepDrftWeb.Services.Data;

namespace DeepDrftWeb.Services.Repositories;

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
        // Two separate queries with no transaction: count and page can be momentarily inconsistent
        // under concurrent writes. Acceptable — write volume is low and the UI is read-only.
        // If filtering is added, the count query must be updated to apply the same filter.
        var count = await _db.Tracks.CountAsync();

        var orderBy = pageParameters.OrderBy ?? (t => t.Id);
        var ordered = pageParameters.IsDescending
            ? _db.Tracks.OrderByDescending(orderBy)
            : _db.Tracks.OrderBy(orderBy);

        var page = await ordered
            .Skip(pageParameters.Skip)
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
            throw new InvalidOperationException($"Track not found: {track.Id}");
        }
        
        trackEntity.Album = track.Album;
        trackEntity.Artist = track.Artist;
        trackEntity.Genre = track.Genre;
        trackEntity.ImagePath = track.ImagePath;
        trackEntity.EntryKey = track.EntryKey;
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