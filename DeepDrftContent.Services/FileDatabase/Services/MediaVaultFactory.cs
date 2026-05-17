using DeepDrftContent.Services.FileDatabase.Models;

namespace DeepDrftContent.Services.FileDatabase.Services;

/// <summary>
/// Factory for creating media vaults
/// </summary>
public static class MediaVaultFactory
{
    public static async Task<MediaVault?> From(string rootPath, MediaVaultType mediaType, IndexFactoryService? factoryService = null)
    {
        return mediaType switch
        {
            MediaVaultType.Image => await ImageVault.FromAsync(rootPath, factoryService),
            MediaVaultType.Audio => await AudioVault.FromAsync(rootPath, factoryService),
            _ => null
        };
    }
}