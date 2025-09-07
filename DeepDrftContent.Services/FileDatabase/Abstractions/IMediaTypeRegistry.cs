using DeepDrftContent.Services.FileDatabase.Models;
using DeepDrftContent.Services.FileDatabase.Services;

namespace DeepDrftContent.Services.FileDatabase.Abstractions;

/// <summary>
/// Interface for registering media type factories
/// </summary>
public interface IMediaTypeRegistry
{
    /// <summary>
    /// Register a factory for a specific media vault type
    /// </summary>
    void RegisterMediaType<TBinary, TParams, TDto, TMetaData, TVault>(MediaVaultType vaultType)
        where TBinary : FileBinary
        where TParams : FileBinaryParams
        where TDto : FileBinaryDto
        where TMetaData : MetaData;

    /// <summary>
    /// Create a binary object from parameters
    /// </summary>
    FileBinary CreateBinary(MediaVaultType vaultType, object parameters);

    /// <summary>
    /// Create a binary object from DTO
    /// </summary>
    FileBinary CreateBinaryFromDto(MediaVaultType vaultType, object dto);

    /// <summary>
    /// Create a DTO from binary object
    /// </summary>
    FileBinaryDto CreateDto(MediaVaultType vaultType, FileBinary binary);

    /// <summary>
    /// Create metadata from media object
    /// </summary>
    MetaData CreateMetaDataFromMedia(MediaVaultType vaultType, string entryKey, string extension, object media);

    /// <summary>
    /// Create parameters from binary and metadata
    /// </summary>
    object CreateParams(MediaVaultType vaultType, FileBinary fileBinary, MetaData metaData);

    /// <summary>
    /// Create media vault
    /// </summary>
    Task<MediaVault?> CreateVaultAsync(MediaVaultType vaultType, string rootPath);

    /// <summary>
    /// Get the binary type for a vault type
    /// </summary>
    Type GetBinaryType(MediaVaultType vaultType);

    /// <summary>
    /// Get the DTO type for a vault type
    /// </summary>
    Type GetDtoType(MediaVaultType vaultType);

    /// <summary>
    /// Get the parameters type for a vault type
    /// </summary>
    Type GetParamsType(MediaVaultType vaultType);

    /// <summary>
    /// Get the metadata type for a vault type
    /// </summary>
    Type GetMetaDataType(MediaVaultType vaultType);

    /// <summary>
    /// Get the vault type for a binary type (reverse mapping)
    /// </summary>
    MediaVaultType GetVaultType(Type binaryType);

    /// <summary>
    /// Get the vault type for a binary type using generics (reverse mapping)
    /// </summary>
    MediaVaultType GetVaultType<T>() where T : FileBinary;
}
