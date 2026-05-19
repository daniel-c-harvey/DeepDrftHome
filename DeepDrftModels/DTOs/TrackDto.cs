using Models.Models;

namespace DeepDrftModels.DTOs;

// Inherits Id, CreatedAt, UpdatedAt from BaseModel (Cerebellum.BlazorBlocks.Models).
// BlazorBlocks's Manager<> generic constraint requires `new()` on the model type, which
// disqualifies `required` properties (the `new()` constraint and required members do not
// compose). EntryKey/TrackName/Artist therefore drop `required` here — the TrackEntity
// side remains required, and TrackConverter assigns every field on the round-trip so an
// empty default is never observable in production code paths.
public class TrackDto : BaseModel
{
    public string EntryKey { get; set; } = string.Empty;
    public string TrackName { get; set; } = string.Empty;
    public string Artist { get; set; } = string.Empty;
    public string? Album { get; set; }
    public string? Genre { get; set; }
    public DateOnly? ReleaseDate { get; set; }
    public string? ImagePath { get; set; }
    public long? CreatedByUserId { get; set; }
}
