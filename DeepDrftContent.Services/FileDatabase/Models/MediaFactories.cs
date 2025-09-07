using DeepDrftContent.Services.FileDatabase.Abstractions;
using DeepDrftContent.Services.FileDatabase.Services;

namespace DeepDrftContent.Services.FileDatabase.Models;

/// <summary>
/// Type mappings for media vault types - simple dictionary-based approach
/// </summary>
public static class MediaVaultTypeMap
{
    private static readonly IMediaTypeRegistry _registry = new SimpleMediaTypeRegistry();

    public static Type GetBinaryType(MediaVaultType vaultType) => _registry.GetBinaryType(vaultType);

    public static Type GetDtoType(MediaVaultType vaultType) => _registry.GetDtoType(vaultType);

    public static Type GetParamsType(MediaVaultType vaultType) => _registry.GetParamsType(vaultType);

    public static Type GetMetaDataType(MediaVaultType vaultType) => _registry.GetMetaDataType(vaultType);

    /// <summary>
    /// Get the vault type for a binary type (reverse mapping)
    /// </summary>
    public static MediaVaultType GetVaultType(Type binaryType) => _registry.GetVaultType(binaryType);

    /// <summary>
    /// Get the vault type for a binary type using generics (reverse mapping)
    /// </summary>
    public static MediaVaultType GetVaultType<T>() where T : FileBinary => _registry.GetVaultType<T>();
}

/// <summary>
/// Factory for creating metadata objects based on vault type
/// </summary>
public static class MetaDataFactory
{
    public static MetaData Create(MediaVaultType type, string entryKey, string extension)
    {
        return type switch
        {
            MediaVaultType.Media => new MetaData(entryKey, extension),
            MediaVaultType.Image => throw new ArgumentException("Image metadata requires aspect ratio. Use CreateImageMetaData instead."),
            MediaVaultType.Audio => throw new ArgumentException("Audio metadata requires duration and bitrate. Use CreateAudioMetaData instead."),
            _ => throw new ArgumentException($"Unknown vault type: {type}")
        };
    }

    public static ImageMetaData CreateImageMetaData(string entryKey, string extension, double aspectRatio)
    {
        return new ImageMetaData(entryKey, extension, aspectRatio);
    }

    public static AudioMetaData CreateAudioMetaData(string entryKey, string extension, double duration, int bitrate)
    {
        return new AudioMetaData(entryKey, extension, duration, bitrate);
    }

    private static readonly IMediaTypeRegistry _metaDataRegistry = new SimpleMediaTypeRegistry();

    public static MetaData CreateFromMedia(MediaVaultType type, string entryKey, string extension, object media)
    {
        return _metaDataRegistry.CreateMetaDataFromMedia(type, entryKey, extension, media);
    }

    public static T Create<T>(MediaVaultType type, string entryKey, string extension) 
        where T : MetaData
    {
        var metaData = Create(type, entryKey, extension);
        return (T)metaData;
    }
}

/// <summary>
/// Factory for creating media parameter objects - simple dictionary-based approach
/// </summary>
public static class MediaParamsFactory
{
    private static readonly IMediaTypeRegistry _registry = new SimpleMediaTypeRegistry();

    public static object Create(MediaVaultType type, FileBinary fileBinary, MetaData metaData)
    {
        return _registry.CreateParams(type, fileBinary, metaData);
    }

    public static T Create<T>(MediaVaultType type, FileBinary fileBinary, MetaData metaData)
    {
        var parameters = Create(type, fileBinary, metaData);
        return (T)parameters;
    }
}

/// <summary>
/// Factory for creating media binary objects - simple dictionary-based approach
/// </summary>
public static class FileBinaryFactory
{
    private static readonly IMediaTypeRegistry _registry = new SimpleMediaTypeRegistry();

    public static object Create(MediaVaultType vaultType, object parameters)
    {
        return _registry.CreateBinary(vaultType, parameters);
    }

    public static T Create<T>(MediaVaultType vaultType, object parameters) where T : FileBinary
    {
        var binary = Create(vaultType, parameters);
        return (T)binary;
    }

    public static object From(MediaVaultType type, object mediaBinaryDto)
    {
        return _registry.CreateBinaryFromDto(type, mediaBinaryDto);
    }

    public static T From<T>(MediaVaultType type, object mediaBinaryDto) where T : FileBinary
    {
        var binary = From(type, mediaBinaryDto);
        return (T)binary;
    }
}

/// <summary>
/// Factory for creating DTO objects from media binaries - simple dictionary-based approach
/// </summary>
public static class FileBinaryDtoFactory
{
    private static readonly IMediaTypeRegistry _registry = new SimpleMediaTypeRegistry();

    public static object From(MediaVaultType type, object mediaBinary)
    {
        if (mediaBinary is not FileBinary fileBinary)
            throw new ArgumentException($"Expected FileBinary but got {mediaBinary.GetType()}");

        return _registry.CreateDto(type, fileBinary);
    }

    public static T From<T>(MediaVaultType type, object mediaBinary)
    {
        var dto = From(type, mediaBinary);
        return (T)dto;
    }
}
