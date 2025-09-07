using System.Text.Json.Serialization;

namespace DeepDrftContent.Services.FileDatabase.Models;

/// <summary>
/// Base metadata for media entries
/// </summary>
/// <param name="MediaKey">The key used to identify the media file</param>
/// <param name="Extension">The file extension of the media</param>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "$type")]
[JsonDerivedType(typeof(MetaData), typeDiscriminator: "media")]
[JsonDerivedType(typeof(ImageMetaData), typeDiscriminator: "image")]
[JsonDerivedType(typeof(AudioMetaData), typeDiscriminator: "audio")]
public record MetaData(string MediaKey, string Extension);

/// <summary>
/// Extended metadata for image entries, including aspect ratio
/// </summary>
/// <param name="MediaKey">The key used to identify the media file</param>
/// <param name="Extension">The file extension of the media</param>
/// <param name="AspectRatio">The aspect ratio of the image</param>
public record ImageMetaData(string MediaKey, string Extension, double AspectRatio) 
    : MetaData(MediaKey, Extension);

/// <summary>
/// Extended metadata for audio entries, including duration and bitrate
/// </summary>
/// <param name="MediaKey">The key used to identify the media file</param>
/// <param name="Extension">The file extension of the media</param>
/// <param name="Duration">The duration of the audio in seconds</param>
/// <param name="Bitrate">The bitrate of the audio in kbps</param>
public record AudioMetaData(string MediaKey, string Extension, double Duration, int Bitrate) 
    : MetaData(MediaKey, Extension);
