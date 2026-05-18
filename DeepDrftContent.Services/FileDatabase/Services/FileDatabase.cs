using DeepDrftContent.Services.FileDatabase.Models;
using DeepDrftContent.Services.FileDatabase.Utils;
using Microsoft.Extensions.Logging;

namespace DeepDrftContent.Services.FileDatabase.Services;

/// <summary>
/// Main file database class that orchestrates multiple media vaults.
/// Includes file watching for automatic index reloading when modified by external processes.
/// </summary>
public class FileDatabase : DirectoryIndexDirectory, IDisposable
{
    private readonly StructuralMap<string, MediaVault> _vaults;
    private readonly IndexWatcher _indexWatcher;
    private readonly IndexFactoryService _indexFactory;
    private readonly ILogger<FileDatabase> _logger;
    private bool _disposed;

    /// <summary>
    /// Factory method to create a FileDatabase instance
    /// </summary>
    public static async Task<FileDatabase?> FromAsync(string rootPath, ILogger<FileDatabase>? logger = null)
    {
        var factoryService = new IndexFactoryService();
        var rootIndex = await factoryService.LoadOrCreateDirectoryIndexAsync(rootPath);

        if (rootIndex != null)
        {
            var db = new FileDatabase(rootPath, rootIndex, factoryService, logger);
            await db.InitVaultsAsync();
            return db;
        }

        return null;
    }

    private FileDatabase(string rootPath, IDirectoryIndex index, IndexFactoryService indexFactory, ILogger<FileDatabase>? logger = null) : base(rootPath, index)
    {
        _vaults = new StructuralMap<string, MediaVault>();
        _indexWatcher = new IndexWatcher();
        _indexFactory = indexFactory;
        _logger = logger ?? Microsoft.Extensions.Logging.Abstractions.NullLogger<FileDatabase>.Instance;
    }

    /// <summary>
    /// Initializes all vaults found in the index
    /// </summary>
    private async Task InitVaultsAsync()
    {
        foreach (var vaultId in GetIndexEntries())
        {
            var vaultType = await GetVaultTypeFromIndex(vaultId);
            if (vaultType.HasValue)
            {
                await InitVaultAsync(vaultId, vaultType.Value);
            }
        }
    }

    /// <summary>
    /// Initializes a specific vault and sets up file watching for its index
    /// </summary>
    private async Task InitVaultAsync(string vaultId, MediaVaultType vaultType)
    {
        var path = Path.Combine(RootPath, vaultId);
        var directoryVault = await MediaVaultFactory.From(path, vaultType, _indexFactory);

        if (directoryVault != null)
        {
            _vaults.Set(vaultId, directoryVault);

            // Watch the vault's index file for external modifications
            _indexWatcher.Watch(path, () =>
            {
                // Reload the index asynchronously when file changes
                _ = directoryVault.ReloadIndexAsync();
            });
        }
    }

    /// <summary>
    /// Gets vault type from the vault's index file
    /// </summary>
    private async Task<MediaVaultType?> GetVaultTypeFromIndex(string vaultId)
    {
        try
        {
            var vaultPath = Path.Combine(RootPath, vaultId);
            var index = await _indexFactory.LoadIndexAsync(IndexType.Vault, vaultPath);
            
            if (index is VaultIndex vaultIndex)
            {
                return vaultIndex.VaultType;
            }
        }
        catch
        {
            // If we can't load the index, we can't determine the vault type
            // This might happen for legacy vaults or corrupted indexes
        }
        
        return null;
    }

    /// <summary>
    /// Checks if a vault exists for the given vault ID
    /// </summary>
    public bool HasVault(string vaultId)
    {
        return _vaults.Has(vaultId);
    }

    /// <summary>
    /// Gets a vault by vault ID
    /// </summary>
    public MediaVault? GetVault(string vaultId)
    {
        return HasVault(vaultId) ? _vaults.Get(vaultId) : null;
    }

    /// <summary>
    /// Creates a new vault. Propagates exceptions to the caller — vault creation failure is not
    /// silently swallowable because a partially-created vault would leave the index inconsistent.
    /// </summary>
    public async Task CreateVaultAsync(string vaultId, MediaVaultType vaultType)
    {
        var path = Path.Combine(RootPath, vaultId);
        var directoryVault = await MediaVaultFactory.From(path, vaultType, _indexFactory);

        if (directoryVault != null)
        {
            _vaults.Set(vaultId, directoryVault);
            await AddToIndexAsync(vaultId);
        }
    }

    /// <summary>
    /// Loads a resource from a specific vault (MediaVaultType inferred from T)
    /// </summary>
    public async Task<T?> LoadResourceAsync<T>(string vaultId, string entryId) 
        where T : FileBinary
    {
        try
        {
            var vault = _vaults.Get(vaultId);
            if (vault != null)
            {
                return await vault.GetEntryAsync<T>(entryId);
            }
        }
        catch
        {
            // Swallow exceptions and return null, matching TypeScript behavior
        }
        
        return null;
    }

    /// <summary>
    /// Registers a resource in a specific vault (MediaVaultType inferred from media type)
    /// </summary>
    public async Task<bool> RegisterResourceAsync(string vaultId, string entryId, FileBinary media)
    {
        try
        {
            var directoryVault = _vaults.Get(vaultId);
            if (directoryVault != null)
            {
                await directoryVault.AddEntryAsync(entryId, media);
                return true;
            }
        }
        catch
        {
            // Swallow exceptions and return false, matching TypeScript behavior
        }

        return false;
    }

    /// <summary>
    /// Removes a resource from a specific vault. Returns null if the vault does not exist,
    /// false if the entry was not found, true if the entry was removed. Distinguishing
    /// "no such vault" / "no such entry" / "removed" lets the HTTP host map cleanly to
    /// 404 vs. 200. Follows the FileDatabase error-swallow contract: any unexpected failure
    /// returns null so callers can surface 5xx without try/catch at the controller layer.
    /// </summary>
    public async Task<bool?> RemoveResourceAsync(string vaultId, string entryId)
    {
        try
        {
            var directoryVault = _vaults.Get(vaultId);
            if (directoryVault == null)
                return null;

            return await directoryVault.RemoveEntryAsync(entryId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "RemoveResourceAsync failed for vault {VaultName} key {Key}", vaultId, entryId);
            return null;
        }
    }

    /// <summary>
    /// Gets all vault IDs managed by this database
    /// </summary>
    public IReadOnlyList<string> GetVaultIds()
    {
        return _vaults.Keys.ToList().AsReadOnly();
    }

    /// <summary>
    /// Gets the total number of vaults
    /// </summary>
    public int GetVaultCount()
    {
        return _vaults.Size;
    }

    /// <summary>
    /// Disposes the file database and stops all file watchers
    /// </summary>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _indexWatcher.Dispose();
        GC.SuppressFinalize(this);
    }
}
