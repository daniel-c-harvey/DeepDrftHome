using System.Text.RegularExpressions;
using DeepDrftContent.FileDatabase.Models;
using DeepDrftContent.FileDatabase.Utils;

namespace DeepDrftContent.FileDatabase.Services;

/// <summary>
/// Abstract base class for media vaults that store and manage media files
/// </summary>
public abstract class MediaVault : VaultIndexDirectory
{
    protected MediaVault(string rootPath, VaultIndex index) : base(rootPath, index) { }

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
        
        if (!HasIndexEntry(entryId))
            return null;

        if (Index is not VaultIndex vaultIndex)
            return null;

        var metaData = vaultIndex.GetEntry(entryId);
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
    private ImageVault(string rootPath, VaultIndex index) : base(rootPath, index) { }

    /// <summary>
    /// Factory method to create an ImageVault instance
    /// </summary>
    public static async Task<ImageVault?> FromAsync(string rootPath)
    {
        var factoryService = new IndexFactoryService();
        var index = await factoryService.LoadOrCreateVaultIndexAsync(rootPath, MediaVaultType.Image);

        if (index != null)
        {
            return new ImageVault(rootPath, (VaultIndex)index);
        }

        return null;
    }
}

public class AudioVault : MediaVault
{
    private AudioVault(string rootPath, VaultIndex index) : base(rootPath, index) { }
    
    public static async Task<AudioVault?> FromAsync(string rootPath)
    {
        var factoryService = new IndexFactoryService();
        var index = await factoryService.LoadOrCreateVaultIndexAsync(rootPath, MediaVaultType.Audio);

        if (index != null)
        {
            return new AudioVault(rootPath, (VaultIndex)index);
        }

        return null;
    }
}
