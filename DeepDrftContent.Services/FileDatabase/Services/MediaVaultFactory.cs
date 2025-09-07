using DeepDrftContent.Services.FileDatabase.Abstractions;
using DeepDrftContent.Services.FileDatabase.Models;

namespace DeepDrftContent.Services.FileDatabase.Services;

/// <summary>
/// Factory for creating media vaults
/// </summary>
public static class MediaVaultFactory
{
    private static readonly IMediaTypeRegistry _registry = new SimpleMediaTypeRegistry();

    public static async Task<MediaVault?> From(string rootPath, MediaVaultType mediaType)
    {
        return await _registry.CreateVaultAsync(mediaType, rootPath);
    }
}