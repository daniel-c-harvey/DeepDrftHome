namespace DeepDrftContent.FileDatabase.Models;

/// <summary>
/// Type mappings for media vault types to their corresponding classes
/// </summary>
public static class MediaVaultTypeMap
{
    public static Type GetBinaryType(MediaVaultType vaultType) => vaultType switch
    {
        MediaVaultType.Media => typeof(MediaBinary),
        MediaVaultType.Image => typeof(ImageBinary),
        _ => throw new ArgumentException($"Unknown vault type: {vaultType}")
    };

    public static Type GetDtoType(MediaVaultType vaultType) => vaultType switch
    {
        MediaVaultType.Media => typeof(MediaBinaryDto),
        MediaVaultType.Image => typeof(ImageBinaryDto),
        _ => throw new ArgumentException($"Unknown vault type: {vaultType}")
    };

    public static Type GetParamsType(MediaVaultType vaultType) => vaultType switch
    {
        MediaVaultType.Media => typeof(MediaBinaryParams),
        MediaVaultType.Image => typeof(ImageBinaryParams),
        _ => throw new ArgumentException($"Unknown vault type: {vaultType}")
    };

    public static Type GetMetaDataType(MediaVaultType vaultType) => vaultType switch
    {
        MediaVaultType.Media => typeof(MetaData),
        MediaVaultType.Image => typeof(ImageMetaData),
        _ => throw new ArgumentException($"Unknown vault type: {vaultType}")
    };
}

/// <summary>
/// Factory for creating metadata objects based on vault type
/// </summary>
public static class MetaDataFactory
{
    public static MetaData Create(MediaVaultType type, string entryKey, string extension, double aspectRatio = 1.0)
    {
        return type switch
        {
            MediaVaultType.Media => new MetaData(entryKey, extension),
            MediaVaultType.Image => new ImageMetaData(entryKey, extension, aspectRatio),
            _ => throw new ArgumentException($"Unknown vault type: {type}")
        };
    }

    public static T Create<T>(MediaVaultType type, string entryKey, string extension, double aspectRatio = 1.0) 
        where T : MetaData
    {
        var metaData = Create(type, entryKey, extension, aspectRatio);
        return (T)metaData;
    }
}

/// <summary>
/// Factory for creating media parameter objects
/// </summary>
public static class MediaParamsFactory
{
    public static object Create(MediaVaultType type, FileBinary fileBinary, MetaData metaData)
    {
        return type switch
        {
            MediaVaultType.Media => new MediaBinaryParams(fileBinary.Buffer, fileBinary.Size, metaData.Extension),
            MediaVaultType.Image when metaData is ImageMetaData imageMetaData => 
                new ImageBinaryParams(fileBinary.Buffer, fileBinary.Size, metaData.Extension, imageMetaData.AspectRatio),
            _ => throw new ArgumentException($"Invalid vault type {type} or metadata type mismatch")
        };
    }

    public static T Create<T>(MediaVaultType type, FileBinary fileBinary, MetaData metaData)
    {
        var parameters = Create(type, fileBinary, metaData);
        return (T)parameters;
    }
}

/// <summary>
/// Factory for creating media binary objects from parameters
/// </summary>
public static class FileBinaryFactory
{
    public static object Create(MediaVaultType vaultType, object parameters)
    {
        return vaultType switch
        {
            MediaVaultType.Media when parameters is MediaBinaryParams mediaParams => 
                new MediaBinary(mediaParams),
            MediaVaultType.Image when parameters is ImageBinaryParams imageParams => 
                new ImageBinary(imageParams),
            _ => throw new ArgumentException($"Invalid vault type {vaultType} or parameter type mismatch")
        };
    }

    public static T Create<T>(MediaVaultType vaultType, object parameters) where T : FileBinary
    {
        var binary = Create(vaultType, parameters);
        return (T)binary;
    }

    public static object From(MediaVaultType type, object mediaBinaryDto)
    {
        return type switch
        {
            MediaVaultType.Media when mediaBinaryDto is MediaBinaryDto mediaDto => 
                MediaBinary.From(mediaDto),
            MediaVaultType.Image when mediaBinaryDto is ImageBinaryDto imageDto => 
                ImageBinary.From(imageDto),
            _ => throw new ArgumentException($"Invalid type {type} or DTO type mismatch")
        };
    }

    public static T From<T>(MediaVaultType type, object mediaBinaryDto) where T : FileBinary
    {
        var binary = From(type, mediaBinaryDto);
        return (T)binary;
    }
}

/// <summary>
/// Factory for creating DTO objects from media binaries
/// </summary>
public static class FileBinaryDtoFactory
{
    public static object From(MediaVaultType type, object mediaBinary)
    {
        return type switch
        {
            MediaVaultType.Media when mediaBinary is MediaBinary media => 
                new MediaBinaryDto(media),
            MediaVaultType.Image when mediaBinary is ImageBinary image => 
                new ImageBinaryDto(image),
            _ => throw new ArgumentException($"Invalid type {type} or binary type mismatch")
        };
    }

    public static T From<T>(MediaVaultType type, object mediaBinary)
    {
        var dto = From(type, mediaBinary);
        return (T)dto;
    }
}
