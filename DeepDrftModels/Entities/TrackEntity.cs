namespace DeepDrftModels.Entities;

public class TrackEntity
{
    public long Id { get; set; }
    public required string EntryKey { get; set; }
    public required string TrackName { get; set; }
    public required string Artist { get; set; }
    public string? Album { get; set; }
    public string? Genre { get; set; }
    public DateOnly? ReleaseDate { get; set; }
    public string? ImagePath { get; set; }
}