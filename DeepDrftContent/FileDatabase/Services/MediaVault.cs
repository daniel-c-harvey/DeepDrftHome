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
    /// Adds a new entry to the vault with the specified media data
    /// </summary>
    public async Task AddEntryAsync(MediaVaultType vaultType, EntryKey entryKey, object media)
    {
        // Extract properties from media object based on type
        var (buffer, extension) = ExtractMediaProperties(media);
        
        var mediaPath = GetMediaPathFromEntryKey(entryKey.Key, extension);
        var metaData = MetaDataFactory.Create(vaultType, entryKey.Key, extension, GetAspectRatio(media));
        
        await AddToIndexAsync(entryKey, metaData);
        await FileUtils.PutFileAsync(mediaPath, buffer);
    }

    /// <summary>
    /// Retrieves an entry from the vault
    /// </summary>
    public async Task<T?> GetEntryAsync<T>(MediaVaultType vaultType, EntryKey entryKey) where T : FileBinary
    {
        if (!HasIndexEntry(entryKey))
            return null;

        if (Index is not VaultIndex vaultIndex)
            return null;

        var metaData = vaultIndex.GetEntry(entryKey);
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
    /// Extracts buffer and extension from a media object
    /// </summary>
    private static (byte[] buffer, string extension) ExtractMediaProperties(object media)
    {
        return media switch
        {
            ImageBinary imageBinary => (imageBinary.Buffer, imageBinary.Extension),
            MediaBinary mediaBinary => (mediaBinary.Buffer, mediaBinary.Extension),
            _ => throw new ArgumentException($"Unsupported media type: {media.GetType()}")
        };
    }

    /// <summary>
    /// Extracts aspect ratio from media object if it's an image
    /// </summary>
    private static double GetAspectRatio(object media)
    {
        return media is ImageBinary imageBinary ? imageBinary.AspectRatio : 1.0;
    }
}

/// <summary>
/// Concrete implementation of MediaVault for image storage
/// </summary>
public class ImageDirectoryVault : MediaVault
{
    private ImageDirectoryVault(string rootPath, VaultIndex index) : base(rootPath, index) { }

    /// <summary>
    /// Factory method to create an ImageDirectoryVault instance
    /// </summary>
    public static async Task<ImageDirectoryVault?> FromAsync(string rootPath)
    {
        var factory = new IndexFactory(rootPath, IndexType.Vault);
        var index = await factory.BuildIndexAsync();

        if (index is VaultIndex vaultIndex)
        {
            return new ImageDirectoryVault(rootPath, vaultIndex);
        }

        return null;
    }


}
