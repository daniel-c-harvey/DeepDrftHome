namespace DeepDrftContent.FileDatabase.Models;

/// <summary>
/// Base interface for all index types - minimal contract
/// </summary>
public interface IIndex
{
    /// <summary>
    /// Gets the key identifier for this index
    /// </summary>
    string GetKey();
}

/// <summary>
/// Interface for indexes that support entry queries
/// </summary>
public interface IEntryQueryable : IIndex
{
    /// <summary>
    /// Gets all entry keys in this index
    /// </summary>
    IReadOnlyList<EntryKey> GetEntries();

    /// <summary>
    /// Gets the number of entries in this index
    /// </summary>
    int GetEntriesSize();

    /// <summary>
    /// Checks if the index contains the specified entry key
    /// </summary>
    bool HasEntry(EntryKey entryKey);
}

/// <summary>
/// Interface for indexes that support directory operations
/// </summary>
public interface IDirectoryIndex : IEntryQueryable
{
    /// <summary>
    /// Adds an entry to the directory index
    /// </summary>
    void PutEntry(EntryKey entryKey);
}

/// <summary>
/// Interface for indexes that support vault operations with metadata
/// </summary>
public interface IVaultIndex : IEntryQueryable
{
    /// <summary>
    /// Gets metadata for a specific entry
    /// </summary>
    MetaData? GetEntry(EntryKey entryKey);

    /// <summary>
    /// Adds an entry with metadata to the vault index
    /// </summary>
    void PutEntry(EntryKey entryKey, MetaData metaData);
}
