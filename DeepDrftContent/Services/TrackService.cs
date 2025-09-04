using DeepDrftContent.Constants;
using DeepDrftContent.FileDatabase.Services;
using DeepDrftContent.Processors;
using DeepDrftModels.Entities;

namespace DeepDrftContent.Services;

/// <summary>
/// Service for managing tracks in both SQL and FileDatabase
/// </summary>
public class TrackService
{
    private readonly FileDatabase.Services.FileDatabase _fileDatabase;
    private readonly AudioProcessor _audioProcessor;

    public TrackService(FileDatabase.Services.FileDatabase fileDatabase, AudioProcessor audioProcessor)
    {
        _fileDatabase = fileDatabase;
        _audioProcessor = audioProcessor;
    }

    /// <summary>
    /// Adds a new track from a WAV file to both databases
    /// </summary>
    /// <param name="wavFilePath">Path to the WAV file</param>
    /// <param name="trackName">Name of the track</param>
    /// <param name="artist">Artist name</param>
    /// <param name="album">Optional album name</param>
    /// <param name="genre">Optional genre</param>
    /// <param name="releaseDate">Optional release date</param>
    /// <returns>The track entity with generated ID and media path</returns>
    public async Task<TrackEntity?> AddTrackFromWavAsync(
        string wavFilePath,
        string trackName,
        string artist,
        string? album = null,
        string? genre = null,
        DateOnly? releaseDate = null)
    {
        try
        {
            // Process the WAV file
            var audioBinary = await _audioProcessor.ProcessWavFileAsync(wavFilePath);
            if (audioBinary == null)
            {
                throw new InvalidOperationException("Failed to process WAV file");
            }

            // Generate a unique track ID
            var trackId = Guid.NewGuid().ToString();

            // Ensure tracks vault exists
            if (!_fileDatabase.HasVault(VaultConstants.Tracks))
            {
                await _fileDatabase.CreateVaultAsync(VaultConstants.Tracks, DeepDrftContent.FileDatabase.Models.MediaVaultType.Audio);
            }

            // Store the audio in FileDatabase
            var success = await _fileDatabase.RegisterResourceAsync(VaultConstants.Tracks, trackId, audioBinary);
            if (!success)
            {
                throw new InvalidOperationException("Failed to store audio in FileDatabase");
            }

            // Create the track entity for SQL database
            var trackEntity = new TrackEntity
            {
                EntryKey = trackId, // FileDatabase entry ID
                TrackName = trackName,
                Artist = artist,
                Album = album,
                Genre = genre,
                ReleaseDate = releaseDate
            };

            return trackEntity;
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Failed to add track: {ex.Message}", ex);
        }
    }

    /// <summary>
    /// Retrieves audio binary from FileDatabase
    /// </summary>
    /// <param name="trackId">Track ID (EntryKey)</param>
    /// <returns>Audio binary or null if not found</returns>
    public async Task<DeepDrftContent.FileDatabase.Models.AudioBinary?> GetAudioBinaryAsync(string trackId)
    {
        return await _fileDatabase.LoadResourceAsync<DeepDrftContent.FileDatabase.Models.AudioBinary>(VaultConstants.Tracks, trackId);
    }

    /// <summary>
    /// Checks if FileDatabase is available and tracks vault exists
    /// </summary>
    public bool IsFileDatabaseReady()
    {
        return _fileDatabase.HasVault(VaultConstants.Tracks);
    }

    /// <summary>
    /// Initializes the tracks vault if it doesn't exist
    /// </summary>
    public async Task InitializeTracksVaultAsync()
    {
        if (!_fileDatabase.HasVault(VaultConstants.Tracks))
        {
            await _fileDatabase.CreateVaultAsync(VaultConstants.Tracks, DeepDrftContent.FileDatabase.Models.MediaVaultType.Audio);
        }
    }
}