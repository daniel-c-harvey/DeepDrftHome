namespace DeepDrftContent.FileDatabase.Models;

/// <summary>
/// Represents a key for entries in the file database system.
/// Combines a string key with a media vault type for type-safe operations.
/// </summary>
/// <param name="Key">The string identifier for the entry</param>
/// <param name="Type">The media vault type this entry belongs to</param>
public record EntryKey(string Key, MediaVaultType Type);
