using DeepDrftContent.Data.FileDatabase.Abstractions;
using DeepDrftContent.Data.FileDatabase.Models;
using DeepDrftContent.Data.FileDatabase.Utils;
using Microsoft.Extensions.Logging;

namespace DeepDrftContent.Data.FileDatabase.Services;

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
    protected IEntryQueryable Index { get; set; }

    protected IndexDirectory(string rootPath, IndexType type, IEntryQueryable index, IIndexDataFactory? indexDataFactory = null)
        : base(rootPath, type, indexDataFactory)
    {
        Index = index;
    }

    protected IReadOnlyList<string> GetIndexEntries() => Index.GetEntries();

    public int GetIndexSize() => Index.GetEntriesSize();

    public virtual Task<bool> HasIndexEntry(string entryId) => Task.FromResult(Index.HasEntry(entryId));
}

/// <summary>
/// Directory index directory implementation
/// </summary>
public class DirectoryIndexDirectory : IndexDirectory
{
    private readonly IDirectoryIndex _directoryIndex;
    private readonly SemaphoreSlim _indexLock = new(1, 1);

    public DirectoryIndexDirectory(string rootPath, IDirectoryIndex index, IIndexDataFactory? indexDataFactory = null)
        : base(rootPath, IndexType.Directory, index, indexDataFactory)
    {
        _directoryIndex = index;
    }

    protected async Task AddToIndexAsync(string entryId)
    {
        await _indexLock.WaitAsync();
        try
        {
            _directoryIndex.PutEntry(entryId);
            await SaveIndexAsync(_directoryIndex);
        }
        finally
        {
            _indexLock.Release();
        }
    }
}

/// <summary>
/// Vault index directory implementation with support for index reloading
/// </summary>
public class VaultIndexDirectory : IndexDirectory
{
    private IVaultIndex _vaultIndex;
    private readonly SemaphoreSlim _indexLock = new(1, 1);
    private readonly IndexFactoryService _factoryService;
    private readonly ILogger<VaultIndexDirectory>? _logger;

    public VaultIndexDirectory(string rootPath, IVaultIndex index, IIndexDataFactory? indexDataFactory = null, ILogger<VaultIndexDirectory>? logger = null, IndexFactoryService? factoryService = null)
        : base(rootPath, IndexType.Vault, index, indexDataFactory ?? factoryService)
    {
        _vaultIndex = index;
        _logger = logger;
        _factoryService = factoryService ?? new IndexFactoryService();
    }

    protected async Task AddToIndexAsync(string entryId, MetaData metaData)
    {
        await _indexLock.WaitAsync();
        try
        {
            _vaultIndex.PutEntry(entryId, metaData);
            await SaveIndexAsync(_vaultIndex);
        }
        finally
        {
            _indexLock.Release();
        }
    }

    /// <summary>
    /// Removes an entry from the index under the index lock, persisting on success.
    /// Returns the removed entry's metadata, or null if the entry did not exist.
    /// Caller is responsible for any backing-file cleanup using the returned metadata.
    /// </summary>
    protected async Task<MetaData?> RemoveFromIndexAsync(string entryId)
    {
        await _indexLock.WaitAsync();
        try
        {
            var metaData = _vaultIndex.GetEntry(entryId);
            if (metaData == null)
                return null;

            if (!_vaultIndex.RemoveEntry(entryId))
                return null;

            await SaveIndexAsync(_vaultIndex);
            return metaData;
        }
        finally
        {
            _indexLock.Release();
        }
    }

    /// <summary>
    /// Reloads the index from disk. Called when the index file is modified externally.
    /// </summary>
    public async Task ReloadIndexAsync()
    {
        await _indexLock.WaitAsync();
        try
        {
            var newIndex = await _factoryService.LoadIndexAsync(IndexType.Vault, RootPath);
            if (newIndex is IVaultIndex vaultIndex)
            {
                _vaultIndex = vaultIndex;
                Index = vaultIndex;
                if (_logger != null)
                    _logger.LogDebug("VaultIndexDirectory: Reloaded index for {RootPath}, {EntryCount} entries", RootPath, vaultIndex.GetEntriesSize());
                else
                    Console.WriteLine($"VaultIndexDirectory: Reloaded index for {RootPath}, {vaultIndex.GetEntriesSize()} entries");
            }
        }
        catch (Exception ex)
        {
            if (_logger != null)
                _logger.LogWarning(ex, "VaultIndexDirectory: Failed to reload index for {RootPath}", RootPath);
            else
                Console.WriteLine($"VaultIndexDirectory: Failed to reload index for {RootPath}: {ex.Message}");
        }
        finally
        {
            _indexLock.Release();
        }
    }

    /// <summary>
    /// Thread-safe check for index entry
    /// </summary>
    public override async Task<bool> HasIndexEntry(string entryId)
    {
        await _indexLock.WaitAsync();
        try
        {
            return _vaultIndex.HasEntry(entryId);
        }
        finally
        {
            _indexLock.Release();
        }
    }

    /// <summary>
    /// Thread-safe get entry metadata
    /// </summary>
    public async Task<MetaData?> GetEntryMetadata(string entryId)
    {
        await _indexLock.WaitAsync();
        try
        {
            return _vaultIndex.GetEntry(entryId);
        }
        finally
        {
            _indexLock.Release();
        }
    }
}
