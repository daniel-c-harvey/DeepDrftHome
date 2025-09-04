using DeepDrftContent.FileDatabase.Abstractions;
using DeepDrftContent.FileDatabase.Models;
using DeepDrftContent.FileDatabase.Utils;

namespace DeepDrftContent.FileDatabase.Services;

/// <summary>
/// Enum representing different types of indexes
/// </summary>
public enum IndexType
{
    Directory,
    Vault
}

/// <summary>
/// Abstract base class for index containers
/// </summary>
public abstract class AbstractIndexContainer
{
    protected IndexType Type { get; }
    public string RootPath { get; }
    private readonly IIndexDataFactory _indexDataFactory;

    protected AbstractIndexContainer(string path, IndexType type, IIndexDataFactory? indexDataFactory = null)
    {
        RootPath = path;
        Type = type;
        _indexDataFactory = indexDataFactory ?? new IndexFactoryService();
    }

    public string GetKey() => Path.GetFileName(RootPath);

    protected async Task SaveIndexAsync<T>(T index) where T : IIndex
    {
        var indexPath = Path.Combine(RootPath, "index");
        var indexData = _indexDataFactory.CreateIndexData(Type, index);
        await FileUtils.PutObjectAsync(indexPath, indexData);
    }
}


/// <summary>
/// Abstract base class for directory containers that manage indexes
/// </summary>
public abstract class IndexDirectory : AbstractIndexContainer
{
    protected IEntryQueryable Index { get; }

    protected IndexDirectory(string rootPath, IndexType type, IEntryQueryable index, IIndexDataFactory? indexDataFactory = null) 
        : base(rootPath, type, indexDataFactory)
    {
        Index = index;
    }

    protected IReadOnlyList<string> GetIndexEntries() => Index.GetEntries();

    public int GetIndexSize() => Index.GetEntriesSize();

    public bool HasIndexEntry(string entryId) => Index.HasEntry(entryId);
}

/// <summary>
/// Directory index directory implementation
/// </summary>
public class DirectoryIndexDirectory : IndexDirectory
{
    private readonly IDirectoryIndex _directoryIndex;

    public DirectoryIndexDirectory(string rootPath, IDirectoryIndex index, IIndexDataFactory? indexDataFactory = null) 
        : base(rootPath, IndexType.Directory, index, indexDataFactory) 
    {
        _directoryIndex = index;
    }

    protected async Task AddToIndexAsync(string entryId)
    {
        _directoryIndex.PutEntry(entryId);
        await SaveIndexAsync(_directoryIndex);
    }
}

/// <summary>
/// Vault index directory implementation
/// </summary>
public class VaultIndexDirectory : IndexDirectory
{
    private readonly IVaultIndex _vaultIndex;

    public VaultIndexDirectory(string rootPath, IVaultIndex index, IIndexDataFactory? indexDataFactory = null) 
        : base(rootPath, IndexType.Vault, index, indexDataFactory) 
    {
        _vaultIndex = index;
    }

    protected async Task AddToIndexAsync(string entryId, MetaData metaData)
    {
        _vaultIndex.PutEntry(entryId, metaData);
        await SaveIndexAsync(_vaultIndex);
    }
}
