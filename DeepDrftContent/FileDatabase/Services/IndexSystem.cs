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

    protected AbstractIndexContainer(string path, IndexType type)
    {
        RootPath = path;
        Type = type;
    }

    public string GetKey() => Path.GetFileName(RootPath);

    protected async Task SaveIndexAsync<T>(T index) where T : IIndex
    {
        var indexPath = Path.Combine(RootPath, "index");
        
        object indexData = Type switch
        {
            IndexType.Directory when index is DirectoryIndex dirIndex => DirectoryIndexData.FromIndex(dirIndex),
            IndexType.Vault when index is VaultIndex vaultIndex => VaultIndexData.FromIndex(vaultIndex),
            _ => throw new ArgumentException($"Invalid index type {Type} for index {typeof(T)}")
        };

        await FileUtils.PutObjectAsync(indexPath, indexData);
    }
}

/// <summary>
/// Factory for creating and loading indexes
/// </summary>
public class IndexFactory : AbstractIndexContainer
{
    public IndexFactory(string path, IndexType type) : base(path, type) { }

    /// <summary>
    /// Builds an index by loading existing or creating new
    /// </summary>
    public async Task<IIndex?> BuildIndexAsync()
    {
        try
        {
            return await LoadOrCreateIndexAsync();
        }
        catch
        {
            return null;
        }
    }

    private async Task<IIndex?> LoadOrCreateIndexAsync()
    {
        try
        {
            return await LoadIndexAsync();
        }
        catch
        {
            return await CreateIndexAsync();
        }
    }

    private async Task<IIndex?> LoadIndexAsync()
    {
        var indexPath = Path.Combine(RootPath, "index");
        
        IIndex result = Type switch
        {
            IndexType.Directory => new DirectoryIndex(await FileUtils.FetchObjectAsync<DirectoryIndexData>(indexPath)),
            IndexType.Vault => new VaultIndex(await FileUtils.FetchObjectAsync<VaultIndexData>(indexPath)),
            _ => throw new ArgumentException($"Unknown index type: {Type}")
        };
        return result;
    }

    private async Task<IIndex?> CreateIndexAsync()
    {
        IIndex index = Type switch
        {
            IndexType.Directory => new DirectoryIndex(new DirectoryIndexData(RootPath)),
            IndexType.Vault => new VaultIndex(new VaultIndexData(RootPath)),
            _ => throw new ArgumentException($"Unknown index type: {Type}")
        };

        await FileUtils.MakeVaultDirectoryAsync(RootPath);
        await SaveIndexAsync(index);

        return index;
    }
}

/// <summary>
/// Abstract base class for directory containers that manage indexes
/// </summary>
public abstract class IndexDirectory : AbstractIndexContainer
{
    protected IIndex Index { get; }

    protected IndexDirectory(string rootPath, IndexType type, IIndex index) : base(rootPath, type)
    {
        Index = index;
    }

    protected IReadOnlyList<EntryKey> GetIndexEntries() => Index.GetEntries();

    public int GetIndexSize() => Index.GetEntriesSize();

    public bool HasIndexEntry(EntryKey entryKey) => Index.HasEntry(entryKey);
}

/// <summary>
/// Directory index directory implementation
/// </summary>
public class DirectoryIndexDirectory : IndexDirectory
{
    public DirectoryIndexDirectory(string rootPath, DirectoryIndex index) 
        : base(rootPath, IndexType.Directory, index) { }

    protected async Task AddToIndexAsync(EntryKey entryKey)
    {
        if (Index is DirectoryIndex dirIndex)
        {
            dirIndex.PutEntry(entryKey);
            await SaveIndexAsync(dirIndex);
        }
    }
}

/// <summary>
/// Vault index directory implementation
/// </summary>
public class VaultIndexDirectory : IndexDirectory
{
    public VaultIndexDirectory(string rootPath, VaultIndex index) 
        : base(rootPath, IndexType.Vault, index) { }

    protected async Task AddToIndexAsync(EntryKey entryKey, MetaData metaData)
    {
        if (Index is VaultIndex vaultIndex)
        {
            vaultIndex.PutEntry(entryKey, metaData);
            await SaveIndexAsync(vaultIndex);
        }
    }
}
