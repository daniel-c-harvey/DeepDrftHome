using DeepDrftContent.FileDatabase.Utils;

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
    public List<EntryKey> Entries { get; set; } = new();

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
/// Serializable data for vault indexes
/// </summary>
public class VaultIndexData : IndexData
{
    public List<(EntryKey Key, MetaData Value)> Entries { get; set; } = new();

    public VaultIndexData(string indexKey) : base(indexKey) { }

    public static VaultIndexData FromIndex(VaultIndex index)
    {
        var data = new VaultIndexData(index.GetKey())
        {
            Entries = index.Entries.Select(kvp => (kvp.Key, kvp.Value)).ToList()
        };
        return data;
    }
}

/// <summary>
/// Directory index implementation using StructuralSet for entries
/// </summary>
public class DirectoryIndex : IndexData, IIndex
{
    public StructuralSet<EntryKey> Entries { get; }

    public DirectoryIndex(DirectoryIndexData indexData) : base(indexData.IndexKey)
    {
        Entries = new StructuralSet<EntryKey>();
        // Load entries from data
        foreach (var entry in indexData.Entries)
        {
            Entries.Add(entry);
        }
    }

    public string GetKey() => IndexKey;

    public IReadOnlyList<EntryKey> GetEntries() => Entries.ToList().AsReadOnly();

    public int GetEntriesSize() => Entries.Size;

    public bool HasEntry(EntryKey entryKey) => Entries.Has(entryKey);

    public void PutEntry(EntryKey entryKey) => Entries.Add(entryKey);
}

/// <summary>
/// Vault index implementation using StructuralMap for entries with metadata
/// </summary>
public class VaultIndex : IndexData, IIndex
{
    public StructuralMap<EntryKey, MetaData> Entries { get; }

    public VaultIndex(VaultIndexData indexData) : base(indexData.IndexKey)
    {
        Entries = new StructuralMap<EntryKey, MetaData>();
        // Load entries from data
        foreach (var (key, value) in indexData.Entries)
        {
            Entries.Set(key, value);
        }
    }

    public string GetKey() => IndexKey;

    public IReadOnlyList<EntryKey> GetEntries() => Entries.Keys.ToList().AsReadOnly();

    public int GetEntriesSize() => Entries.Size;

    public bool HasEntry(EntryKey entryKey) => Entries.Has(entryKey);

    public MetaData? GetEntry(EntryKey entryKey) => Entries.Get(entryKey);

    public void PutEntry(EntryKey entryKey, MetaData metaData) => Entries.Set(entryKey, metaData);
}
