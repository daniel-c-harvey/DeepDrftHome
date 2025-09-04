using DeepDrftContent.FileDatabase.Utils;
using System.Text.Json.Serialization;

namespace DeepDrftContent.FileDatabase.Models;

/// <summary>
/// Base class for index data used in serialization
/// </summary>
public abstract class IndexData
{
    public string IndexKey { get; }

    protected IndexData(string indexKey)
    {
        IndexKey = indexKey;
    }
}

/// <summary>
/// Serializable data for directory indexes
/// </summary>
public class DirectoryIndexData : IndexData
{
    public List<string> Entries { get; set; } = new();

    public DirectoryIndexData(string indexKey) : base(indexKey) { }

    public static DirectoryIndexData FromIndex(DirectoryIndex index)
    {
        var data = new DirectoryIndexData(index.GetKey())
        {
            Entries = index.GetEntries().ToList()
        };
        return data;
    }
}

/// <summary>
/// Entry data for vault index serialization
/// </summary>
public class VaultEntryData
{
    public string Key { get; set; } = null!;
    public MetaData Value { get; set; } = null!;
}

/// <summary>
/// Serializable data for vault indexes
/// </summary>
public class VaultIndexData : IndexData
{
    public List<VaultEntryData> Entries { get; set; } = new();
    public MediaVaultType VaultType { get; set; }

    public VaultIndexData(string indexKey) : base(indexKey) 
    {
        VaultType = MediaVaultType.Media; // Default vault type for legacy compatibility
    }
    
    [JsonConstructor]
    public VaultIndexData(string indexKey, MediaVaultType vaultType) : base(indexKey) 
    {
        VaultType = vaultType;
    }

    public static VaultIndexData FromIndex(VaultIndex index)
    {
        var data = new VaultIndexData(index.GetKey(), index.VaultType)
        {
            Entries = index.Entries.Select(kvp => new VaultEntryData { Key = kvp.Key, Value = kvp.Value }).ToList()
        };
        return data;
    }
}

/// <summary>
/// Directory index implementation using StructuralSet for entries
/// </summary>
public class DirectoryIndex : IndexData, IDirectoryIndex
{
    public StructuralSet<string> Entries { get; }

    public DirectoryIndex(DirectoryIndexData indexData) : base(indexData.IndexKey)
    {
        Entries = new StructuralSet<string>();
        // Load entries from data
        foreach (var entry in indexData.Entries)
        {
            Entries.Add(entry);
        }
    }

    public string GetKey() => IndexKey;

    public IReadOnlyList<string> GetEntries() => Entries.ToList().AsReadOnly();

    public int GetEntriesSize() => Entries.Size;

    public bool HasEntry(string entryId) => Entries.Has(entryId);

    public void PutEntry(string entryId) => Entries.Add(entryId);
}

/// <summary>
/// Vault index implementation using StructuralMap for entries with metadata
/// </summary>
public class VaultIndex : IndexData, IVaultIndex
{
    public StructuralMap<string, MetaData> Entries { get; }
    public MediaVaultType VaultType { get; }

    public VaultIndex(VaultIndexData indexData) : base(indexData.IndexKey)
    {
        Entries = new StructuralMap<string, MetaData>();
        VaultType = indexData.VaultType;
        // Load entries from data
        foreach (var entry in indexData.Entries)
        {
            Entries.Set(entry.Key, entry.Value);
        }
    }

    public string GetKey() => IndexKey;

    public IReadOnlyList<string> GetEntries() => Entries.Keys.ToList().AsReadOnly();

    public int GetEntriesSize() => Entries.Size;

    public bool HasEntry(string entryId) => Entries.Has(entryId);

    public MetaData? GetEntry(string entryId) => Entries.Get(entryId);

    public void PutEntry(string entryId, MetaData metaData) => Entries.Set(entryId, metaData);
}
