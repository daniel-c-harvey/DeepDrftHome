using DeepDrftModels.DTOs;
using DeepDrftModels.Entities;
using Models.Converters;

namespace DeepDrftData;

/// <summary>
/// Static entity ↔ DTO converter consumed by the BlazorBlocks Manager base class.
/// The DTO side mirrors the entity field-for-field; the audit columns
/// (CreatedAt, UpdatedAt) come from BaseEntity / BaseModel.
/// IsDeleted does not round-trip — soft-deleted rows are not exposed via the model.
/// </summary>
public class TrackConverter : IEntityToModelConverter<TrackEntity, TrackDto>
{
    public static TrackDto Convert(TrackEntity entity) => new()
    {
        Id = entity.Id,
        CreatedAt = entity.CreatedAt,
        UpdatedAt = entity.UpdatedAt,
        EntryKey = entity.EntryKey,
        TrackName = entity.TrackName,
        Artist = entity.Artist,
        Album = entity.Album,
        Genre = entity.Genre,
        ReleaseDate = entity.ReleaseDate,
        ImagePath = entity.ImagePath,
        CreatedByUserId = entity.CreatedByUserId
    };

    public static TrackEntity Convert(TrackDto model) => new()
    {
        Id = model.Id,
        CreatedAt = model.CreatedAt,
        UpdatedAt = model.UpdatedAt,
        EntryKey = model.EntryKey,
        TrackName = model.TrackName,
        Artist = model.Artist,
        Album = model.Album,
        Genre = model.Genre,
        ReleaseDate = model.ReleaseDate,
        ImagePath = model.ImagePath,
        CreatedByUserId = model.CreatedByUserId
    };
}
