using System.Text.RegularExpressions;
using DeepDrftContent.FileDatabase.Models;
using DeepDrftContent.FileDatabase.Utils;

namespace DeepDrftContent.FileDatabase.Services;

/// <summary>
/// Abstract base class for media vaults that store and manage media files
/// </summary>
public abstract class MediaVault : VaultIndexDirectory
{
    protected MediaVault(string rootPath, VaultIndex index, IndexFactoryService? factoryService = null)
        : base(rootPath, index, factoryService: factoryService) { }

    /// <summary>
    /// Generates a media key from an entry key by sanitizing special characters
    /// </summary>
    protected string GetMediaKey(string entryKey, string extension)
    {
        var sanitized = Regex.Replace(entryKey, @"[^a-zA-Z0-9]", "-");
        return $"{sanitized}{extension}";
    }

    /// <summary>
    /// Gets the full file path for a media file from an entry key
    /// </summary>
    protected string GetMediaPathFromEntryKey(string entryKey, string extension)
    {
        return Path.Combine(RootPath, GetMediaKey(entryKey, extension));
    }

    /// <summary>
    /// Gets the full file path for a media file from a media key
    /// </summary>
    protected string GetMediaPathFromMediaKey(string mediaKey)
    {
        return Path.Combine(RootPath, mediaKey);
    }

    /// <summary>
    /// Adds a new entry to the vault with the specified media data (MediaVaultType inferred from media type)
    /// </summary>
    public async Task AddEntryAsync(string entryId, FileBinary media)
    {
        // Extract properties from media object based on type
        var (buffer, extension) = ExtractMediaProperties(media);
        
        // Infer MediaVaultType from the media object type
        var vaultType = MediaVaultTypeMap.GetVaultType(media.GetType());
        
        var mediaPath = GetMediaPathFromEntryKey(entryId, extension);
        var metaData = MetaDataFactory.CreateFromMedia(vaultType, entryId, extension, media);
        
        // Use string-based index operations
        await AddToIndexAsync(entryId, metaData);
        await FileUtils.PutFileAsync(mediaPath, buffer);
    }

    /// <summary>
    /// Retrieves an entry from the vault (MediaVaultType inferred from T)
    /// </summary>
    public async Task<T?> GetEntryAsync<T>(string entryId) where T : FileBinary
    {
        // Infer MediaVaultType from the generic type T
        var vaultType = MediaVaultTypeMap.GetVaultType<T>();

        // Use thread-safe method from VaultIndexDirectory
        if (!await HasIndexEntry(entryId))
            return null;

        // Use thread-safe metadata retrieval
        var metaData = await GetEntryMetadata(entryId);
        if (metaData == null)
            return null;

        var mediaPath = GetMediaPathFromEntryKey(metaData.MediaKey, metaData.Extension);

        if (!FileUtils.FileExists(mediaPath))
            return null;

        var fileBinary = await FileUtils.FetchFileAsync(mediaPath);
        var parameters = MediaParamsFactory.Create(vaultType, fileBinary, metaData);

        var result = FileBinaryFactory.Create(vaultType, parameters);
        return (T)result;
    }

    /// <summary>
    /// Opens a read-only stream over an entry's backing file plus its metadata
    /// (extension/MIME), without buffering the file into memory.
    /// Returns null if the entry is unknown or the backing file is missing.
    ///
    /// Use this when the caller will forward bytes to a network response — the
    /// existing <see cref="GetEntryAsync{T}"/> allocates a full <c>byte[]</c>
    /// and pushes large WAVs onto the LOH for every request.
    ///
    /// The caller owns the returned stream and must dispose it. Error-handling
    /// follows the same swallow-and-return-null contract as the rest of the
    /// FileDatabase API; the caller checks for null.
    /// </summary>
    public async Task<MediaStream?> GetEntryStreamAsync(string entryId)
    {
        try
        {
            if (!await HasIndexEntry(entryId))
                return null;

            var metaData = await GetEntryMetadata(entryId);
            if (metaData == null)
                return null;

            var mediaPath = GetMediaPathFromEntryKey(metaData.MediaKey, metaData.Extension);
            if (!FileUtils.FileExists(mediaPath))
                return null;

            // Async-capable, sequential-scan FileStream — the response writer will pull
            // bytes in order. bufferSize matches FileUtils.FetchFileAsync (64 KB).
            var stream = new FileStream(
                mediaPath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                bufferSize: 64 * 1024,
                useAsync: true);

            return new MediaStream(stream, metaData.Extension);
        }
        catch
        {
            // Match FileDatabase error-swallow contract.
            return null;
        }
    }

    /// <summary>
    /// Removes an entry from the vault: drops it from the index (persisting the change)
    /// and deletes the backing file from disk. Returns true if an entry was removed,
    /// false if the entry was not present. Follows the FileDatabase error-swallow contract
    /// for read failures; index/file write failures propagate so the caller can map them
    /// to a 5xx.
    /// </summary>
    public async Task<bool> RemoveEntryAsync(string entryId)
    {
        var metaData = await RemoveFromIndexAsync(entryId);
        if (metaData == null)
            return false;

        // Index already persisted; if the file is missing or fails to delete, the entry
        // is still gone from the catalogue. Treat a missing file as success (callers asked
        // for the entry to go away, and it has). A failure deleting an existing file leaves
        // an orphan on disk; surface it to the caller via exception so the host can log,
        // matching the AddEntryAsync error-propagation shape.
        var mediaPath = GetMediaPathFromEntryKey(metaData.MediaKey, metaData.Extension);
        if (FileUtils.FileExists(mediaPath))
        {
            File.Delete(mediaPath);
        }

        return true;
    }

    /// <summary>
    /// Extracts buffer and extension from a media binary
    /// </summary>
    private static (byte[] buffer, string extension) ExtractMediaProperties(FileBinary media)
    {
        return media switch
        {
            ImageBinary imageBinary => (imageBinary.Buffer, imageBinary.Extension),
            AudioBinary audioBinary => (audioBinary.Buffer, audioBinary.Extension),
            MediaBinary mediaBinary => (mediaBinary.Buffer, mediaBinary.Extension),
            FileBinary fileBinary => throw new ArgumentException($"FileBinary must be a specific media type (ImageBinary, AudioBinary, or MediaBinary), not base FileBinary"),
            _ => throw new ArgumentException($"Unsupported media type: {media.GetType()}")
        };
    }
}

/// <summary>
/// Concrete implementation of MediaVault for image storage
/// </summary>
public class ImageVault : MediaVault
{
    private ImageVault(string rootPath, VaultIndex index, IndexFactoryService? factoryService = null)
        : base(rootPath, index, factoryService) { }

    /// <summary>
    /// Factory method to create an ImageVault instance
    /// </summary>
    public static async Task<ImageVault?> FromAsync(string rootPath, IndexFactoryService? factoryService = null)
    {
        var factory = factoryService ?? new IndexFactoryService();
        var index = await factory.LoadOrCreateVaultIndexAsync(rootPath, MediaVaultType.Image);

        if (index != null)
        {
            return new ImageVault(rootPath, (VaultIndex)index, factory);
        }

        return null;
    }
}

public class AudioVault : MediaVault
{
    private AudioVault(string rootPath, VaultIndex index, IndexFactoryService? factoryService = null)
        : base(rootPath, index, factoryService) { }

    public static async Task<AudioVault?> FromAsync(string rootPath, IndexFactoryService? factoryService = null)
    {
        var factory = factoryService ?? new IndexFactoryService();
        var index = await factory.LoadOrCreateVaultIndexAsync(rootPath, MediaVaultType.Audio);

        if (index != null)
        {
            return new AudioVault(rootPath, (VaultIndex)index, factory);
        }

        return null;
    }
}

/// <summary>
/// An open read-only stream over a vault entry plus the extension needed to
/// resolve its MIME type. Caller owns the stream and must dispose it.
/// </summary>
public sealed class MediaStream : IDisposable, IAsyncDisposable
{
    public Stream Stream { get; }
    public string Extension { get; }

    public MediaStream(Stream stream, string extension)
    {
        Stream = stream;
        Extension = extension;
    }

    public void Dispose() => Stream.Dispose();
    public ValueTask DisposeAsync() => Stream.DisposeAsync();
}
