using DeepDrftContent.FileDatabase.Abstractions;
using DeepDrftContent.FileDatabase.Models;

namespace DeepDrftContent.FileDatabase.Services;

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