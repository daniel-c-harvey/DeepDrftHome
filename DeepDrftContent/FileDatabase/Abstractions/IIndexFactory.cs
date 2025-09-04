using DeepDrftContent.FileDatabase.Models;
using IndexType = DeepDrftContent.FileDatabase.Services.IndexType;

namespace DeepDrftContent.FileDatabase.Abstractions;

/// <summary>
/// Interface for creating index instances
/// </summary>
public interface IIndexFactory
{
    /// <summary>
    /// Creates an index of the specified type
    /// </summary>
    Task<IIndex?> CreateIndexAsync(IndexType type, string rootPath);

    /// <summary>
    /// Loads an existing index of the specified type
    /// </summary>
    Task<IIndex?> LoadIndexAsync(IndexType type, string rootPath);

    /// <summary>
    /// Loads existing index or creates new one if loading fails
    /// </summary>
    Task<IIndex?> LoadOrCreateIndexAsync(IndexType type, string rootPath);
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
