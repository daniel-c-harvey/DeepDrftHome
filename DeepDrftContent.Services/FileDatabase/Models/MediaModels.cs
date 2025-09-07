namespace DeepDrftContent.Services.FileDatabase.Models;

/// <summary>
/// Parameters for creating a FileBinary
/// </summary>
/// <param name="Buffer">The binary data</param>
/// <param name="Size">The size of the data in bytes</param>
public record FileBinaryParams(byte[] Buffer, int Size);

/// <summary>
/// Base class for file binary data
/// </summary>
public class FileBinary
{
    public byte[] Buffer { get; }
    public int Size { get; }

    public FileBinary(FileBinaryParams parameters)
    {
        Buffer = parameters.Buffer;
        Size = parameters.Size;
    }

    public static FileBinary From(FileBinaryDto dto)
    {
        var buffer = Convert.FromBase64String(dto.Base64);
        return new FileBinary(new FileBinaryParams(buffer, dto.Size));
    }
}

/// <summary>
/// DTO for FileBinary serialization
/// </summary>
/// <param name="Base64">Base64 encoded binary data</param>
/// <param name="Size">Size of the original data</param>
public record FileBinaryDto(string Base64, int Size)
{
    public FileBinaryDto(FileBinary fileBinary) : this(
        Convert.ToBase64String(fileBinary.Buffer), 
        fileBinary.Size) { }
}

/// <summary>
/// Parameters for creating a MediaBinary
/// </summary>
/// <param name="Buffer">The binary data</param>
/// <param name="Size">The size of the data in bytes</param>
/// <param name="Extension">The file extension</param>
public record MediaBinaryParams(byte[] Buffer, int Size, string Extension) 
    : FileBinaryParams(Buffer, Size);

/// <summary>
/// Media binary with extension information
/// </summary>
public class MediaBinary : FileBinary
{
    public string Extension { get; }

    public MediaBinary(MediaBinaryParams parameters) : base(parameters)
    {
        Extension = parameters.Extension;
    }

    public static MediaBinary From(MediaBinaryDto dto)
    {
        var buffer = Convert.FromBase64String(dto.Base64);
        var extension = GetExtensionType(dto.Mime);
        return new MediaBinary(new MediaBinaryParams(buffer, dto.Size, extension));
    }

    private static string GetExtensionType(string mime)
    {
        return MimeTypeExtensions.GetExtension(mime);
    }
}

/// <summary>
/// DTO for MediaBinary serialization
/// </summary>
/// <param name="Base64">Base64 encoded binary data</param>
/// <param name="Size">Size of the original data</param>
/// <param name="Mime">MIME type of the media</param>
public record MediaBinaryDto(string Base64, int Size, string Mime) : FileBinaryDto(Base64, Size)
{
    public MediaBinaryDto(MediaBinary mediaBinary) : this(
        Convert.ToBase64String(mediaBinary.Buffer),
        mediaBinary.Size,
        MimeTypeExtensions.GetMimeType(mediaBinary.Extension)) { }
}

/// <summary>
/// Parameters for creating an ImageBinary
/// </summary>
/// <param name="Buffer">The binary data</param>
/// <param name="Size">The size of the data in bytes</param>
/// <param name="Extension">The file extension</param>
/// <param name="AspectRatio">The aspect ratio of the image</param>
public record ImageBinaryParams(byte[] Buffer, int Size, string Extension, double AspectRatio) 
    : MediaBinaryParams(Buffer, Size, Extension);

/// <summary>
/// Image binary with aspect ratio information
/// </summary>
public class ImageBinary : MediaBinary
{
    public double AspectRatio { get; }

    public ImageBinary(ImageBinaryParams parameters) : base(parameters)
    {
        AspectRatio = parameters.AspectRatio;
    }

    public static ImageBinary From(ImageBinaryDto dto)
    {
        var buffer = Convert.FromBase64String(dto.Base64);
        var extension = GetExtensionType(dto.Mime);
        return new ImageBinary(new ImageBinaryParams(buffer, dto.Size, extension, dto.AspectRatio));
    }

    private static string GetExtensionType(string mime)
    {
        return MimeTypeExtensions.GetExtension(mime);
    }
}

/// <summary>
/// DTO for ImageBinary serialization
/// </summary>
/// <param name="Base64">Base64 encoded binary data</param>
/// <param name="Size">Size of the original data</param>
/// <param name="Mime">MIME type of the media</param>
/// <param name="AspectRatio">The aspect ratio of the image</param>
public record ImageBinaryDto(string Base64, int Size, string Mime, double AspectRatio) 
    : MediaBinaryDto(Base64, Size, Mime)
{
    public ImageBinaryDto(ImageBinary imageBinary) : this(
        Convert.ToBase64String(imageBinary.Buffer),
        imageBinary.Size,
        MimeTypeExtensions.GetMimeType(imageBinary.Extension),
        imageBinary.AspectRatio) { }
}

/// <summary>
/// Parameters for creating an AudioBinary
/// </summary>
/// <param name="Buffer">The binary data</param>
/// <param name="Size">The size of the data in bytes</param>
/// <param name="Extension">The file extension</param>
/// <param name="Duration">The duration of the audio in seconds</param>
/// <param name="Bitrate">The bitrate of the audio in kbps</param>
public record AudioBinaryParams(byte[] Buffer, int Size, string Extension, double Duration, int Bitrate) 
    : MediaBinaryParams(Buffer, Size, Extension);

/// <summary>
/// Audio binary with duration and bitrate information
/// </summary>
public class AudioBinary : MediaBinary
{
    public double Duration { get; }
    public int Bitrate { get; }

    public AudioBinary(AudioBinaryParams parameters) : base(parameters)
    {
        Duration = parameters.Duration;
        Bitrate = parameters.Bitrate;
    }

    public static AudioBinary From(AudioBinaryDto dto)
    {
        var buffer = Convert.FromBase64String(dto.Base64);
        var extension = GetExtensionType(dto.Mime);
        return new AudioBinary(new AudioBinaryParams(buffer, dto.Size, extension, dto.Duration, dto.Bitrate));
    }

    private static string GetExtensionType(string mime)
    {
        return MimeTypeExtensions.GetExtension(mime);
    }
}

/// <summary>
/// DTO for AudioBinary serialization
/// </summary>
/// <param name="Base64">Base64 encoded binary data</param>
/// <param name="Size">Size of the original data</param>
/// <param name="Mime">MIME type of the media</param>
/// <param name="Duration">The duration of the audio in seconds</param>
/// <param name="Bitrate">The bitrate of the audio in kbps</param>
public record AudioBinaryDto(string Base64, int Size, string Mime, double Duration, int Bitrate) 
    : MediaBinaryDto(Base64, Size, Mime)
{
    public AudioBinaryDto(AudioBinary audioBinary) : this(
        Convert.ToBase64String(audioBinary.Buffer),
        audioBinary.Size,
        MimeTypeExtensions.GetMimeType(audioBinary.Extension),
        audioBinary.Duration,
        audioBinary.Bitrate) { }
}

/// <summary>
/// Utility class for MIME type and extension conversions
/// </summary>
public static class MimeTypeExtensions
{
    private static readonly Dictionary<string, string> MimeTypes = new()
    {
        { ".jpg", "image/jpeg" },
        { ".jpeg", "image/jpeg" },
        { ".png", "image/png" },
        { ".gif", "image/gif" },
        { ".webp", "image/webp" },
        { ".svg", "image/svg+xml" },
        { ".bmp", "image/bmp" },
        { ".mp3", "audio/mpeg" },
        { ".wav", "audio/wav" },
        { ".flac", "audio/flac" },
        { ".aac", "audio/aac" },
        { ".ogg", "audio/ogg" },
        { ".m4a", "audio/mp4" }
    };

    private static readonly Dictionary<string, string> Extensions = new()
    {
        { "image/jpeg", ".jpg" },
        { "image/png", ".png" },
        { "image/gif", ".gif" },
        { "image/webp", ".webp" },
        { "image/svg+xml", ".svg" },
        { "image/bmp", ".bmp" },
        { "audio/mpeg", ".mp3" },
        { "audio/wav", ".wav" },
        { "audio/flac", ".flac" },
        { "audio/aac", ".aac" },
        { "audio/ogg", ".ogg" },
        { "audio/mp4", ".m4a" }
    };

    public static string GetMimeType(string extension)
    {
        return MimeTypes.TryGetValue(extension.ToLowerInvariant(), out var mime) 
            ? mime 
            : "application/octet-stream";
    }

    public static string GetExtension(string mime)
    {
        return Extensions.TryGetValue(mime.ToLowerInvariant(), out var extension) 
            ? extension 
            : ".bin";
    }
}
