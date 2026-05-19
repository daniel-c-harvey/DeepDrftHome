using Data.Data.Repositories;
using Data.Errors;
using DeepDrftData.Data;
using DeepDrftModels.Entities;
using Microsoft.Extensions.Logging;

namespace DeepDrftData.Repositories;

public class TrackRepository : Repository<DeepDrftContext, TrackEntity>
{
    public TrackRepository(
        DeepDrftContext context,
        ILogger<Repository<DeepDrftContext, TrackEntity>> logger,
        IDbExceptionClassifier? classifier = null)
        : base(context, logger, classifier: classifier)
    {
    }

    protected override void UpdateEntity(TrackEntity target, TrackEntity source)
    {
        base.UpdateEntity(target, source); // copies CreatedAt, UpdatedAt, IsDeleted
        target.EntryKey = source.EntryKey;
        target.TrackName = source.TrackName;
        target.Artist = source.Artist;
        target.Album = source.Album;
        target.Genre = source.Genre;
        target.ReleaseDate = source.ReleaseDate;
        target.ImagePath = source.ImagePath;
        target.CreatedByUserId = source.CreatedByUserId;
    }
}
