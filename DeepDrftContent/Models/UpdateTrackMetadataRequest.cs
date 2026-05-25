namespace DeepDrftContent.Models;

/// <summary>
/// Body of <c>PUT api/track/meta/{id}</c>. Metadata-only — EntryKey is immutable and never
/// travels over this surface.
/// </summary>
public record UpdateTrackMetadataRequest(
    string TrackName,
    string Artist,
    string? Album,
    string? Genre,
    DateOnly? ReleaseDate);
