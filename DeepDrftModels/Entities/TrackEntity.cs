namespace DeepDrftModels.Entities;

public class TrackEntity
{
    public long Id { get; set; }
    public string MediaPath { get; set; }
    public string TrackName { get; set; }
    public string Artist { get; set; }
    public string? Album { get; set; }
    public string? Genre { get; set; }
    public DateOnly? ReleaseDate { get; set; }
    public string? ImagePath { get; set; }
}