using DeepDrftContent.Services.FileDatabase.Abstractions;
using DeepDrftContent.Services.FileDatabase.Models;

namespace DeepDrftContent.Services.FileDatabase.Services;

/// <summary>
/// Simple dictionary-based registry for media type factories
/// </summary>
public class SimpleMediaTypeRegistry : IMediaTypeRegistry
{
    private readonly Dictionary<MediaVaultType, Func<object, FileBinary>> _binaryFactories = new();
    private readonly Dictionary<MediaVaultType, Func<object, FileBinary>> _binaryFromDtoFactories = new();
    private readonly Dictionary<MediaVaultType, Func<FileBinary, FileBinaryDto>> _dtoFactories = new();
    private readonly Dictionary<MediaVaultType, Func<string, string, object, MetaData>> _metaDataFromMediaFactories = new();
    private readonly Dictionary<MediaVaultType, Func<FileBinary, MetaData, object>> _paramsFactories = new();
    private readonly Dictionary<MediaVaultType, Func<string, Task<MediaVault?>>> _vaultFactories = new();
    private readonly Dictionary<MediaVaultType, Type> _binaryTypes = new();
    private readonly Dictionary<MediaVaultType, Type> _dtoTypes = new();
    private readonly Dictionary<MediaVaultType, Type> _paramsTypes = new();
    private readonly Dictionary<MediaVaultType, Type> _metaDataTypes = new();
    
    // Reverse mapping: Type -> MediaVaultType
    private readonly Dictionary<Type, MediaVaultType> _typeToVaultType = new();

    public SimpleMediaTypeRegistry()
    {
        // Clean one-line registrations with generics - no reflection!
        RegisterType<MediaBinary, MediaBinaryParams, MediaBinaryDto, MetaData>(
            MediaVaultType.Media,
            p => new MediaBinary(p),
            dto => MediaBinary.From(dto),
            binary => new MediaBinaryDto(binary),
            (key, ext, _) => new MetaData(key, ext),
            (binary, meta) => new MediaBinaryParams(binary.Buffer, binary.Size, meta.Extension));

        RegisterType<ImageBinary, ImageBinaryParams, ImageBinaryDto, ImageMetaData>(
            MediaVaultType.Image,
            p => new ImageBinary(p),
            dto => ImageBinary.From(dto),
            binary => new ImageBinaryDto(binary),
            (key, ext, media) => media is ImageBinary img ? new ImageMetaData(key, ext, img.AspectRatio) : new MetaData(key, ext),
            (binary, meta) => meta is ImageMetaData imgMeta 
                ? new ImageBinaryParams(binary.Buffer, binary.Size, meta.Extension, imgMeta.AspectRatio)
                : throw new ArgumentException("ImageBinary requires ImageMetaData"),
            async path => await ImageVault.FromAsync(path));

        RegisterType<AudioBinary, AudioBinaryParams, AudioBinaryDto, AudioMetaData>(
            MediaVaultType.Audio,
            p => new AudioBinary(p),
            dto => AudioBinary.From(dto),
            binary => new AudioBinaryDto(binary),
            (key, ext, media) => media is AudioBinary audio ? new AudioMetaData(key, ext, audio.Duration, audio.Bitrate) : new MetaData(key, ext),
            (binary, meta) => meta is AudioMetaData audioMeta
                ? new AudioBinaryParams(binary.Buffer, binary.Size, meta.Extension, audioMeta.Duration, audioMeta.Bitrate)
                : throw new ArgumentException("AudioBinary requires AudioMetaData"),
            async path => await AudioVault.FromAsync(path));
    }

    private void RegisterType<TBinary, TParams, TDto, TMetaData>(
        MediaVaultType vaultType,
        Func<TParams, TBinary> binaryFactory,
        Func<TDto, TBinary> binaryFromDtoFactory,
        Func<TBinary, TDto> dtoFactory,
        Func<string, string, object, MetaData> metaDataFactory,
        Func<FileBinary, MetaData, object> paramsFactory,
        Func<string, Task<MediaVault?>>? vaultFactory = null)
        where TBinary : FileBinary
        where TParams : FileBinaryParams
        where TDto : FileBinaryDto
        where TMetaData : MetaData
    {
        _binaryFactories[vaultType] = p => binaryFactory((TParams)p);
        _binaryFromDtoFactories[vaultType] = dto => binaryFromDtoFactory((TDto)dto);
        _dtoFactories[vaultType] = binary => dtoFactory((TBinary)binary);
        _metaDataFromMediaFactories[vaultType] = metaDataFactory;
        _paramsFactories[vaultType] = paramsFactory;
        _binaryTypes[vaultType] = typeof(TBinary);
        _dtoTypes[vaultType] = typeof(TDto);
        _paramsTypes[vaultType] = typeof(TParams);
        _metaDataTypes[vaultType] = typeof(TMetaData);
        
        // Populate reverse mapping
        _typeToVaultType[typeof(TBinary)] = vaultType;
        
        if (vaultFactory != null)
            _vaultFactories[vaultType] = vaultFactory;
    }

    // Public interface implementation - allows external registration
    public void RegisterMediaType<TBinary, TParams, TDto, TMetaData, TVault>(MediaVaultType vaultType)
        where TBinary : FileBinary
        where TParams : FileBinaryParams
        where TDto : FileBinaryDto
        where TMetaData : MetaData
    {
        // For now, we can't auto-generate the factories without reflection
        // This would need to be implemented if external registration is needed
        throw new NotImplementedException("Use RegisterType method for internal registration. External registration not yet implemented.");
    }


    public FileBinary CreateBinary(MediaVaultType vaultType, object parameters)
    {
        return _binaryFactories.TryGetValue(vaultType, out var factory) 
            ? factory(parameters)
            : throw new ArgumentException($"Unknown vault type: {vaultType}");
    }

    public FileBinary CreateBinaryFromDto(MediaVaultType vaultType, object dto)
    {
        return _binaryFromDtoFactories.TryGetValue(vaultType, out var factory)
            ? factory(dto)
            : throw new ArgumentException($"Unknown vault type: {vaultType}");
    }

    public FileBinaryDto CreateDto(MediaVaultType vaultType, FileBinary binary)
    {
        return _dtoFactories.TryGetValue(vaultType, out var factory)
            ? factory(binary)
            : throw new ArgumentException($"Unknown vault type: {vaultType}");
    }

    public MetaData CreateMetaDataFromMedia(MediaVaultType vaultType, string entryKey, string extension, object media)
    {
        return _metaDataFromMediaFactories.TryGetValue(vaultType, out var factory)
            ? factory(entryKey, extension, media)
            : throw new ArgumentException($"Unknown vault type: {vaultType}");
    }

    public object CreateParams(MediaVaultType vaultType, FileBinary fileBinary, MetaData metaData)
    {
        return _paramsFactories.TryGetValue(vaultType, out var factory)
            ? factory(fileBinary, metaData)
            : throw new ArgumentException($"Unknown vault type: {vaultType}");
    }

    public async Task<MediaVault?> CreateVaultAsync(MediaVaultType vaultType, string rootPath)
    {
        return _vaultFactories.TryGetValue(vaultType, out var factory)
            ? await factory(rootPath)
            : null;
    }

    public Type GetBinaryType(MediaVaultType vaultType) => 
        _binaryTypes.TryGetValue(vaultType, out var type) ? type : throw new ArgumentException($"Unknown vault type: {vaultType}");

    public Type GetDtoType(MediaVaultType vaultType) => 
        _dtoTypes.TryGetValue(vaultType, out var type) ? type : throw new ArgumentException($"Unknown vault type: {vaultType}");

    public Type GetParamsType(MediaVaultType vaultType) => 
        _paramsTypes.TryGetValue(vaultType, out var type) ? type : throw new ArgumentException($"Unknown vault type: {vaultType}");

    public Type GetMetaDataType(MediaVaultType vaultType) => 
        _metaDataTypes.TryGetValue(vaultType, out var type) ? type : throw new ArgumentException($"Unknown vault type: {vaultType}");

    public MediaVaultType GetVaultType(Type binaryType)
    {
        if (_typeToVaultType.TryGetValue(binaryType, out var vaultType))
            return vaultType;
            
        // Check inheritance hierarchy for derived types
        foreach (var kvp in _typeToVaultType)
        {
            if (kvp.Key.IsAssignableFrom(binaryType))
                return kvp.Value;
        }
        
        throw new ArgumentException($"Cannot infer MediaVaultType for {binaryType.Name}. Type not registered.");
    }

    public MediaVaultType GetVaultType<T>() where T : FileBinary
        => GetVaultType(typeof(T));
}
