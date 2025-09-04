using System.Text.Json.Serialization;

namespace DeepDrftContent.FileDatabase.Models;

/// <summary>
/// Base metadata for media entries
/// </summary>
/// <param name="MediaKey">The key used to identify the media file</param>
/// <param name="Extension">The file extension of the media</param>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "$type")]
[JsonDerivedType(typeof(MetaData), typeDiscriminator: "media")]
[JsonDerivedType(typeof(ImageMetaData), typeDiscriminator: "image")]
public record MetaData(string MediaKey, string Extension);

/// <summary>
/// Extended metadata for image entries, including aspect ratio
/// </summary>
/// <param name="MediaKey">The key used to identify the media file</param>
/// <param name="Extension">The file extension of the media</param>
/// <param name="AspectRatio">The aspect ratio of the image</param>
public record ImageMetaData(string MediaKey, string Extension, double AspectRatio) 
    : MetaData(MediaKey, Extension);
