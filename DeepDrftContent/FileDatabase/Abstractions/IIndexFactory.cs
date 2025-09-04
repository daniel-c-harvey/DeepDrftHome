using DeepDrftContent.FileDatabase.Models;
using IndexType = DeepDrftContent.FileDatabase.Services.IndexType;

namespace DeepDrftContent.FileDatabase.Abstractions;

/// <summary>
/// Interface for creating index instances
/// </summary>
public interface IIndexFactory
{
    /// <summary>
    /// Loads an existing index of the specified type
    /// </summary>
    Task<IIndex?> LoadIndexAsync(IndexType type, string rootPath);

    /// <summary>
    /// Creates a directory index
    /// </summary>
    Task<IDirectoryIndex?> CreateDirectoryIndexAsync(string rootPath);
    
    /// <summary>
    /// Loads existing directory index or creates new one if loading fails
    /// </summary>
    Task<IDirectoryIndex?> LoadOrCreateDirectoryIndexAsync(string rootPath);
    
    /// <summary>
    /// Creates a vault index with the specified vault type
    /// </summary>
    Task<IVaultIndex?> CreateVaultIndexAsync(string rootPath, MediaVaultType vaultType);
    
    /// <summary>
    /// Loads existing vault index or creates new one with the specified vault type if loading fails
    /// </summary>
    Task<IVaultIndex?> LoadOrCreateVaultIndexAsync(string rootPath, MediaVaultType vaultType);
}

/// <summary>
/// Interface for creating index data objects
/// </summary>
public interface IIndexDataFactory
{
    /// <summary>
    /// Creates index data for serialization
    /// </summary>
    object CreateIndexData(IndexType type, IIndex index);

    /// <summary>
    /// Creates index instance from data
    /// </summary>
    IIndex CreateIndexFromData(IndexType type, object indexData);
}
