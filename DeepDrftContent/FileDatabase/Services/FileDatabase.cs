using DeepDrftContent.FileDatabase.Models;
using DeepDrftContent.FileDatabase.Utils;

namespace DeepDrftContent.FileDatabase.Services;

/// <summary>
/// Main file database class that orchestrates multiple media vaults
/// </summary>
public class FileDatabase : DirectoryIndexDirectory
{
    private readonly StructuralMap<EntryKey, MediaVault> _vaults;

    /// <summary>
    /// Factory method to create a FileDatabase instance
    /// </summary>
    public static async Task<FileDatabase?> FromAsync(string rootPath)
    {
        var factory = new IndexFactory(rootPath, IndexType.Directory);
        var rootIndex = await factory.BuildIndexAsync();

        if (rootIndex is DirectoryIndex directoryIndex)
        {
            var db = new FileDatabase(rootPath, directoryIndex);
            await db.InitVaultsAsync();
            return db;
        }

        return null;
    }

    private FileDatabase(string rootPath, DirectoryIndex index) : base(rootPath, index)
    {
        _vaults = new StructuralMap<EntryKey, MediaVault>();
    }

    /// <summary>
    /// Initializes all vaults found in the index
    /// </summary>
    private async Task InitVaultsAsync()
    {
        foreach (var vaultKey in GetIndexEntries())
        {
            await InitVaultAsync(vaultKey);
        }
    }

    /// <summary>
    /// Initializes a specific vault
    /// </summary>
    private async Task InitVaultAsync(EntryKey vaultKey)
    {
        var path = Path.Combine(RootPath, vaultKey.Key);
        var directoryVault = await ImageDirectoryVault.FromAsync(path);

        if (directoryVault != null)
        {
            _vaults.Set(vaultKey, directoryVault);
        }
    }

    /// <summary>
    /// Checks if a vault exists for the given key
    /// </summary>
    public bool HasVault(EntryKey vaultKey)
    {
        return _vaults.Has(vaultKey);
    }

    /// <summary>
    /// Gets a vault by key
    /// </summary>
    public MediaVault? GetVault(EntryKey vaultKey)
    {
        return HasVault(vaultKey) ? _vaults.Get(vaultKey) : null;
    }

    /// <summary>
    /// Creates a new vault
    /// </summary>
    public async Task CreateVaultAsync(EntryKey vaultKey)
    {
        try
        {
            var path = Path.Combine(RootPath, vaultKey.Key);
            var directoryVault = await ImageDirectoryVault.FromAsync(path);

            if (directoryVault != null)
            {
                _vaults.Set(vaultKey, directoryVault);
                await AddToIndexAsync(vaultKey);
            }
        }
        catch
        {
            // Re-throw to maintain the same error behavior as TypeScript version
            throw;
        }
    }

    /// <summary>
    /// Loads a resource from a specific vault
    /// </summary>
    public async Task<T?> LoadResourceAsync<T>(MediaVaultType vaultType, EntryKey vaultKey, EntryKey entryKey) 
        where T : FileBinary
    {
        try
        {
            var vault = _vaults.Get(vaultKey);
            if (vault != null)
            {
                return await vault.GetEntryAsync<T>(vaultType, entryKey);
            }
        }
        catch
        {
            // Swallow exceptions and return null, matching TypeScript behavior
        }
        
        return null;
    }

    /// <summary>
    /// Registers a resource in a specific vault
    /// </summary>
    public async Task<bool> RegisterResourceAsync(MediaVaultType vaultType, EntryKey vaultKey, EntryKey entryKey, object media)
    {
        try
        {
            var directoryVault = _vaults.Get(vaultKey);
            if (directoryVault != null)
            {
                await directoryVault.AddEntryAsync(vaultType, entryKey, media);
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
    /// Gets all vault keys managed by this database
    /// </summary>
    public IReadOnlyList<EntryKey> GetVaultKeys()
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
