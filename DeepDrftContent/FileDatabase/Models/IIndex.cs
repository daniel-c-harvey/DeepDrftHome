namespace DeepDrftContent.FileDatabase.Models;

/// <summary>
/// Base interface for all index types
/// </summary>
public interface IIndex
{
    /// <summary>
    /// Gets the key identifier for this index
    /// </summary>
    string GetKey();

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
