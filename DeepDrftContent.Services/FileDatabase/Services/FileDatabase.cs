using DeepDrftContent.Services.FileDatabase.Models;
using DeepDrftContent.Services.FileDatabase.Utils;

namespace DeepDrftContent.Services.FileDatabase.Services;

/// <summary>
/// Main file database class that orchestrates multiple media vaults
/// </summary>
public class FileDatabase : DirectoryIndexDirectory
{
    private readonly StructuralMap<string, MediaVault> _vaults;

    /// <summary>
    /// Factory method to create a FileDatabase instance
    /// </summary>
    public static async Task<FileDatabase?> FromAsync(string rootPath)
    {
        var factoryService = new IndexFactoryService();
        var rootIndex = await factoryService.LoadOrCreateDirectoryIndexAsync(rootPath);

        if (rootIndex != null)
        {
            var db = new FileDatabase(rootPath, rootIndex);
            await db.InitVaultsAsync();
            return db;
        }

        return null;
    }

    private FileDatabase(string rootPath, IDirectoryIndex index) : base(rootPath, index)
    {
        _vaults = new StructuralMap<string, MediaVault>();
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
    /// Initializes a specific vault
    /// </summary>
    private async Task InitVaultAsync(string vaultId, MediaVaultType vaultType)
    {
        var path = Path.Combine(RootPath, vaultId);
        var directoryVault = await MediaVaultFactory.From(path, vaultType);

        if (directoryVault != null)
        {
            _vaults.Set(vaultId, directoryVault);
        }
    }

    /// <summary>
    /// Gets vault type from the vault's index file
    /// </summary>
    private async Task<MediaVaultType?> GetVaultTypeFromIndex(string vaultId)
    {
        try
        {
            var factoryService = new IndexFactoryService();
            var vaultPath = Path.Combine(RootPath, vaultId);
            var index = await factoryService.LoadIndexAsync(IndexType.Vault, vaultPath);
            
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
    /// Creates a new vault
    /// </summary>
    public async Task CreateVaultAsync(string vaultId, MediaVaultType vaultType)
    {
        try
        {
            var path = Path.Combine(RootPath, vaultId);
            var directoryVault = await MediaVaultFactory.From(path, vaultType);

            if (directoryVault != null)
            {
                _vaults.Set(vaultId, directoryVault);
                // Now using string-based index
                await AddToIndexAsync(vaultId);
            }
        }
        catch
        {
            throw;
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

}
