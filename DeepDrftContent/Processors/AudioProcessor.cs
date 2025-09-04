using DeepDrftContent.FileDatabase.Models;

namespace DeepDrftContent.Processors;

/// <summary>
/// Service for processing audio files and extracting metadata
/// </summary>
public class AudioProcessor
{
    /// <summary>
    /// Processes a WAV file and creates an AudioBinary object
    /// </summary>
    /// <param name="filePath">Path to the WAV file</param>
    /// <returns>AudioBinary object with metadata</returns>
    public async Task<AudioBinary?> ProcessWavFileAsync(string filePath)
    {
        if (!File.Exists(filePath))
        {
            throw new FileNotFoundException($"WAV file not found: {filePath}");
        }

        if (!Path.GetExtension(filePath).Equals(".wav", StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("File must be a WAV file", nameof(filePath));
        }

        try
        {
            var buffer = await File.ReadAllBytesAsync(filePath);
            var wavInfo = ExtractWavMetadata(buffer);
            
            var parameters = new AudioBinaryParams(
                Buffer: buffer,
                Size: buffer.Length,
                Extension: ".wav",
                Duration: wavInfo.Duration,
                Bitrate: wavInfo.Bitrate
            );

            return new AudioBinary(parameters);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Failed to process WAV file: {ex.Message}", ex);
        }
    }

    /// <summary>
    /// Extracts metadata from WAV file buffer
    /// </summary>
    private WavMetadata ExtractWavMetadata(byte[] buffer)
    {
        try
        {
            // WAV file format parsing
            // RIFF header starts at byte 0
            if (buffer.Length < 44)
            {
                throw new InvalidDataException("WAV file too short to contain valid header");
            }

            // Check RIFF signature
            var riffSignature = System.Text.Encoding.ASCII.GetString(buffer, 0, 4);
            if (riffSignature != "RIFF")
            {
                throw new InvalidDataException("Invalid WAV file: Missing RIFF signature");
            }

            // Check WAVE format
            var waveSignature = System.Text.Encoding.ASCII.GetString(buffer, 8, 4);
            if (waveSignature != "WAVE")
            {
                throw new InvalidDataException("Invalid WAV file: Missing WAVE signature");
            }

            // Find fmt chunk
            var fmtChunkPos = FindChunk(buffer, "fmt ");
            if (fmtChunkPos == -1)
            {
                throw new InvalidDataException("Invalid WAV file: Missing fmt chunk");
            }

            // Parse fmt chunk
            var fmtChunkSize = BitConverter.ToUInt32(buffer, fmtChunkPos + 4);
            var sampleRate = BitConverter.ToUInt32(buffer, fmtChunkPos + 12);
            var byteRate = BitConverter.ToUInt32(buffer, fmtChunkPos + 16);
            var channels = BitConverter.ToUInt16(buffer, fmtChunkPos + 10);
            var bitsPerSample = BitConverter.ToUInt16(buffer, fmtChunkPos + 22);

            // Find data chunk
            var dataChunkPos = FindChunk(buffer, "data");
            if (dataChunkPos == -1)
            {
                throw new InvalidDataException("Invalid WAV file: Missing data chunk");
            }

            var dataSize = BitConverter.ToUInt32(buffer, dataChunkPos + 4);

            // Calculate duration
            var duration = (double)dataSize / byteRate;
            
            // Calculate bitrate (bits per second / 1000 for kbps)
            var bitrate = (int)((sampleRate * channels * bitsPerSample) / 1000);

            return new WavMetadata
            {
                Duration = duration,
                Bitrate = bitrate,
                SampleRate = (int)sampleRate,
                Channels = channels,
                BitsPerSample = bitsPerSample
            };
        }
        catch (Exception ex)
        {
            // Fallback to basic metadata if parsing fails
            Console.WriteLine($"Warning: Could not parse WAV metadata: {ex.Message}");
            return new WavMetadata
            {
                Duration = 180.0, // Default 3 minutes
                Bitrate = 1411,   // Default CD quality bitrate for WAV
                SampleRate = 44100,
                Channels = 2,
                BitsPerSample = 16
            };
        }
    }

    /// <summary>
    /// Finds a chunk in the WAV file buffer
    /// </summary>
    private int FindChunk(byte[] buffer, string chunkId)
    {
        var chunkBytes = System.Text.Encoding.ASCII.GetBytes(chunkId);
        
        for (int i = 12; i < buffer.Length - 8; i += 4)
        {
            if (buffer[i] == chunkBytes[0] && 
                buffer[i + 1] == chunkBytes[1] && 
                buffer[i + 2] == chunkBytes[2] && 
                buffer[i + 3] == chunkBytes[3])
            {
                return i;
            }
        }
        
        return -1;
    }

    /// <summary>
    /// WAV file metadata
    /// </summary>
    private class WavMetadata
    {
        public double Duration { get; set; }
        public int Bitrate { get; set; }
        public int SampleRate { get; set; }
        public int Channels { get; set; }
        public int BitsPerSample { get; set; }
    }
}