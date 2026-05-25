using DeepDrftContent.FileDatabase.Abstractions;
using DeepDrftContent.FileDatabase.Models;
using DeepDrftContent.FileDatabase.Utils;
using IndexType = DeepDrftContent.FileDatabase.Services.IndexType;

namespace DeepDrftContent.FileDatabase.Services;

/// <summary>
/// Factory service for creating and managing indexes
/// </summary>
public class IndexFactoryService : IIndexFactory, IIndexDataFactory
{
    private readonly Dictionary<IndexType, Func<object, IIndex>> _indexFromDataCreators;
    private readonly Dictionary<IndexType, Func<IIndex, object>> _indexDataCreators;

    public IndexFactoryService()
    {
        _indexFromDataCreators = new Dictionary<IndexType, Func<object, IIndex>>
        {
            { IndexType.Directory, data => new DirectoryIndex((DirectoryIndexData)data) },
            { IndexType.Vault, data => new VaultIndex((VaultIndexData)data) }
        };

        _indexDataCreators = new Dictionary<IndexType, Func<IIndex, object>>
        {
            { IndexType.Directory, index => DirectoryIndexData.FromIndex((DirectoryIndex)index) },
            { IndexType.Vault, index => VaultIndexData.FromIndex((VaultIndex)index) }
        };
    }

    public async Task<IDirectoryIndex?> CreateDirectoryIndexAsync(string rootPath)
    {
        var indexData = new DirectoryIndexData(Path.GetFileName(rootPath));
        var index = new DirectoryIndex(indexData);
        
        // Ensure directory exists and save the index
        await FileUtils.MakeVaultDirectoryAsync(rootPath);
        await SaveIndexAsync(rootPath, IndexType.Directory, index);

        return index;
    }

    public async Task<IIndex?> LoadIndexAsync(IndexType type, string rootPath)
    {
        if (!_indexFromDataCreators.TryGetValue(type, out var creator))
        {
            throw new ArgumentException($"Unknown index type: {type}");
        }

        var indexPath = Path.Combine(rootPath, "index");
        
        object indexData = type switch
        {
            IndexType.Directory => await FileUtils.FetchObjectAsync<DirectoryIndexData>(indexPath),
            IndexType.Vault => await FileUtils.FetchObjectAsync<VaultIndexData>(indexPath),
            _ => throw new ArgumentException($"Unknown index type: {type}")
        };

        return creator(indexData);
    }

    public async Task<IDirectoryIndex?> LoadOrCreateDirectoryIndexAsync(string rootPath)
    {
        try
        {
            var index = await LoadIndexAsync(IndexType.Directory, rootPath);
            return index as IDirectoryIndex;
        }
        catch
        {
            return await CreateDirectoryIndexAsync(rootPath);
        }
    }

    public async Task<IVaultIndex?> CreateVaultIndexAsync(string rootPath, MediaVaultType vaultType)
    {
        var vaultIndexData = new VaultIndexData(Path.GetFileName(rootPath), vaultType);
        var index = new VaultIndex(vaultIndexData);
        
        // Ensure directory exists and save the index
        await FileUtils.MakeVaultDirectoryAsync(rootPath);
        await SaveIndexAsync(rootPath, IndexType.Vault, index);

        return index;
    }

    public async Task<IVaultIndex?> LoadOrCreateVaultIndexAsync(string rootPath, MediaVaultType vaultType)
    {
        try
        {
            var index = await LoadIndexAsync(IndexType.Vault, rootPath);
            return index as IVaultIndex;
        }
        catch
        {
            return await CreateVaultIndexAsync(rootPath, vaultType);
        }
    }

    public object CreateIndexData(IndexType type, IIndex index)
    {
        if (!_indexDataCreators.TryGetValue(type, out var creator))
        {
            throw new ArgumentException($"Unknown index type: {type}");
        }

        return creator(index);
    }

    public IIndex CreateIndexFromData(IndexType type, object indexData)
    {
        if (!_indexFromDataCreators.TryGetValue(type, out var creator))
        {
            throw new ArgumentException($"Unknown index type: {type}");
        }

        return creator(indexData);
    }

    private async Task SaveIndexAsync(string rootPath, IndexType type, IIndex index)
    {
        var indexPath = Path.Combine(rootPath, "index");
        var indexData = CreateIndexData(type, index);
        await FileUtils.PutObjectAsync(indexPath, indexData);
    }
}
