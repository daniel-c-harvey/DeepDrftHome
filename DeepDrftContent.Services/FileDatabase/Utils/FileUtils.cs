using System.Text.Json;
using DeepDrftContent.Services.FileDatabase.Models;

namespace DeepDrftContent.Services.FileDatabase.Utils;

/// <summary>
/// Utility class for file I/O operations, matching the TypeScript file utilities
/// </summary>
public static class FileUtils
{
    /// <summary>
    /// Reads a file and returns it as a FileBinary object
    /// </summary>
    /// <param name="mediaPath">Path to the media file</param>
    /// <returns>FileBinary containing the file data</returns>
    public static async Task<FileBinary> FetchFileAsync(string mediaPath)
    {
        using var fileStream = new FileStream(mediaPath, FileMode.Open, FileAccess.Read, FileShare.Read, bufferSize: 64 * 1024);
        
        var buffer = new byte[fileStream.Length];
        var totalBytesRead = 0;
        
        while (totalBytesRead < fileStream.Length)
        {
            var bytesRead = await fileStream.ReadAsync(buffer.AsMemory(totalBytesRead));
            if (bytesRead == 0)
                throw new EndOfStreamException($"Unexpected end of stream while reading {mediaPath}");
            
            totalBytesRead += bytesRead;
        }

        return new FileBinary(new FileBinaryParams(buffer, buffer.Length));
    }

    /// <summary>
    /// Writes binary data to a file
    /// </summary>
    /// <param name="mediaPath">Path where to write the file</param>
    /// <param name="buffer">Binary data to write</param>
    public static async Task PutFileAsync(string mediaPath, byte[] buffer)
    {
        const int chunkSize = 64 * 1024;
        
        using var fileStream = new FileStream(mediaPath, FileMode.Create, FileAccess.Write, FileShare.None, bufferSize: chunkSize);
        
        for (int offset = 0; offset < buffer.Length; offset += chunkSize)
        {
            var length = Math.Min(chunkSize, buffer.Length - offset);
            await fileStream.WriteAsync(buffer.AsMemory(offset, length));
        }
        
        await fileStream.FlushAsync();
    }

    /// <summary>
    /// Serializes an object to a file using JSON
    /// </summary>
    /// <param name="filePath">Path to the file</param>
    /// <param name="obj">Object to serialize</param>
    public static async Task PutObjectAsync<T>(string filePath, T obj)
    {
        var options = new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };

        var json = JsonSerializer.Serialize(obj, options);
        await File.WriteAllTextAsync(filePath, json);
    }

    /// <summary>
    /// Deserializes an object from a JSON file
    /// </summary>
    /// <param name="filePath">Path to the file</param>
    /// <returns>Deserialized object</returns>
    public static async Task<T> FetchObjectAsync<T>(string filePath)
    {
        var json = await File.ReadAllTextAsync(filePath);
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };

        var result = JsonSerializer.Deserialize<T>(json, options);
        return result ?? throw new InvalidOperationException($"Failed to deserialize object from {filePath}");
    }

    /// <summary>
    /// Creates a directory if it doesn't exist
    /// </summary>
    /// <param name="directoryPath">Path to the directory</param>
    public static Task MakeVaultDirectoryAsync(string directoryPath)
    {
        Directory.CreateDirectory(directoryPath);
        return Task.CompletedTask;
    }

    /// <summary>
    /// Checks if a file exists
    /// </summary>
    /// <param name="filePath">Path to check</param>
    /// <returns>True if file exists</returns>
    public static bool FileExists(string filePath)
    {
        return File.Exists(filePath);
    }

    /// <summary>
    /// Checks if a directory exists
    /// </summary>
    /// <param name="directoryPath">Path to check</param>
    /// <returns>True if directory exists</returns>
    public static bool DirectoryExists(string directoryPath)
    {
        return Directory.Exists(directoryPath);
    }
}
