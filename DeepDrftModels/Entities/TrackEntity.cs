using Models.Entities;

namespace DeepDrftModels.Entities;

// Inherits Id, CreatedAt, UpdatedAt, IsDeleted from BaseEntity (Cerebellum.BlazorBlocks.Models).
// BaseEntity ships the audit columns but does not declare IEntity itself, so subclasses
// declare it explicitly to satisfy the generic constraints on Repository<>/Manager<>/etc.
public class TrackEntity : BaseEntity, IEntity
{
    public required string EntryKey { get; set; }
    public required string TrackName { get; set; }
    public required string Artist { get; set; }
    public string? Album { get; set; }
    public string? Genre { get; set; }
    public DateOnly? ReleaseDate { get; set; }
    public string? ImagePath { get; set; }
    public long? CreatedByUserId { get; set; }
}
