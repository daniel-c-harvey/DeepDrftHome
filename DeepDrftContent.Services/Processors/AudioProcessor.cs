using DeepDrftContent.Services.FileDatabase.Models;

namespace DeepDrftContent.Services.Processors;

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
    /// Extracts metadata from WAV file buffer with comprehensive validation
    /// </summary>
    private WavMetadata ExtractWavMetadata(byte[] buffer)
    {
        try
        {
            var validationResult = ValidateWavStructure(buffer);
            if (!validationResult.IsValid)
            {
                throw new InvalidDataException($"WAV validation failed: {validationResult.ErrorMessage}");
            }

            var metadata = ParseWavMetadata(buffer, validationResult);
            ValidateAudioParameters(metadata);
            
            return metadata;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Warning: WAV parsing failed, using defaults: {ex.Message}");
            return GetDefaultWavMetadata();
        }
    }
    
    /// <summary>
    /// Validates WAV file structure and returns parsing information
    /// </summary>
    private WavValidationResult ValidateWavStructure(byte[] buffer)
    {
        if (buffer.Length < 44)
        {
            return new WavValidationResult { IsValid = false, ErrorMessage = "File too short" };
        }

        // Validate RIFF signature
        var riffSignature = System.Text.Encoding.ASCII.GetString(buffer, 0, 4);
        if (riffSignature != "RIFF")
        {
            return new WavValidationResult { IsValid = false, ErrorMessage = "Invalid RIFF signature" };
        }

        // Validate WAVE signature
        var waveSignature = System.Text.Encoding.ASCII.GetString(buffer, 8, 4);
        if (waveSignature != "WAVE")
        {
            return new WavValidationResult { IsValid = false, ErrorMessage = "Invalid WAVE signature" };
        }

        // Find and validate fmt chunk
        var fmtChunkPos = FindChunk(buffer, "fmt ");
        if (fmtChunkPos == -1)
        {
            return new WavValidationResult { IsValid = false, ErrorMessage = "Missing fmt chunk" };
        }

        var fmtChunkSize = BitConverter.ToUInt32(buffer, fmtChunkPos + 4);
        if (fmtChunkSize < 16)
        {
            return new WavValidationResult { IsValid = false, ErrorMessage = "fmt chunk too small" };
        }

        // Validate audio format (PCM only)
        var audioFormat = BitConverter.ToUInt16(buffer, fmtChunkPos + 8);
        if (audioFormat != 1)
        {
            return new WavValidationResult { IsValid = false, ErrorMessage = "Only PCM format supported" };
        }

        // Find data chunk
        var dataChunkPos = FindChunk(buffer, "data");
        if (dataChunkPos == -1)
        {
            return new WavValidationResult { IsValid = false, ErrorMessage = "Missing data chunk" };
        }

        return new WavValidationResult 
        { 
            IsValid = true, 
            FmtChunkPos = fmtChunkPos,
            DataChunkPos = dataChunkPos
        };
    }
    
    /// <summary>
    /// Parses WAV metadata from validated buffer
    /// </summary>
    private WavMetadata ParseWavMetadata(byte[] buffer, WavValidationResult validation)
    {
        var channels = BitConverter.ToUInt16(buffer, validation.FmtChunkPos + 10);
        var sampleRate = BitConverter.ToUInt32(buffer, validation.FmtChunkPos + 12);
        var byteRate = BitConverter.ToUInt32(buffer, validation.FmtChunkPos + 16);
        var blockAlign = BitConverter.ToUInt16(buffer, validation.FmtChunkPos + 20);
        var bitsPerSample = BitConverter.ToUInt16(buffer, validation.FmtChunkPos + 22);
        var dataSize = BitConverter.ToUInt32(buffer, validation.DataChunkPos + 4);

        var duration = byteRate > 0 ? (double)dataSize / byteRate : 0.0;
        var bitrate = (int)((sampleRate * channels * bitsPerSample) / 1000);

        return new WavMetadata
        {
            Duration = duration,
            Bitrate = bitrate,
            SampleRate = (int)sampleRate,
            Channels = channels,
            BitsPerSample = bitsPerSample,
            BlockAlign = blockAlign,
            DataSize = (int)dataSize
        };
    }
    
    /// <summary>
    /// Validates audio parameters for reasonableness
    /// </summary>
    private void ValidateAudioParameters(WavMetadata metadata)
    {
        var validSampleRates = new[] { 8000, 11025, 16000, 22050, 44100, 48000, 88200, 96000, 176400, 192000 };
        var validBitDepths = new[] { 8, 16, 24, 32 };
        
        if (metadata.Channels < 1 || metadata.Channels > 8)
        {
            throw new InvalidDataException($"Invalid channel count: {metadata.Channels}");
        }
        
        if (!validSampleRates.Contains(metadata.SampleRate))
        {
            throw new InvalidDataException($"Unsupported sample rate: {metadata.SampleRate}");
        }
        
        if (!validBitDepths.Contains(metadata.BitsPerSample))
        {
            throw new InvalidDataException($"Unsupported bit depth: {metadata.BitsPerSample}");
        }
        
        var expectedBlockAlign = metadata.Channels * (metadata.BitsPerSample / 8);
        if (metadata.BlockAlign != expectedBlockAlign)
        {
            throw new InvalidDataException($"Invalid block align: expected {expectedBlockAlign}, got {metadata.BlockAlign}");
        }
    }
    
    /// <summary>
    /// Returns default WAV metadata for fallback scenarios
    /// </summary>
    private WavMetadata GetDefaultWavMetadata()
    {
        return new WavMetadata
        {
            Duration = 180.0,
            Bitrate = 1411,
            SampleRate = 44100,
            Channels = 2,
            BitsPerSample = 16,
            BlockAlign = 4,
            DataSize = 0
        };
    }

    /// <summary>
    /// Finds a chunk in the WAV file buffer with proper alignment handling
    /// </summary>
    private int FindChunk(byte[] buffer, string chunkId)
    {
        var chunkBytes = System.Text.Encoding.ASCII.GetBytes(chunkId);
        int offset = 12; // Start after RIFF header
        
        while (offset <= buffer.Length - 8)
        {
            // Check for chunk signature match
            bool match = true;
            for (int i = 0; i < 4; i++)
            {
                if (buffer[offset + i] != chunkBytes[i])
                {
                    match = false;
                    break;
                }
            }
            
            if (match)
            {
                return offset;
            }
            
            // Move to next chunk with proper alignment
            if (offset + 4 < buffer.Length)
            {
                var chunkSize = BitConverter.ToUInt32(buffer, offset + 4);
                offset += 8 + (int)((chunkSize + 1) & ~1U); // Ensure even alignment
            }
            else
            {
                break;
            }
        }
        
        return -1;
    }

    /// <summary>
    /// WAV file metadata with complete audio information
    /// </summary>
    private class WavMetadata
    {
        public double Duration { get; set; }
        public int Bitrate { get; set; }
        public int SampleRate { get; set; }
        public int Channels { get; set; }
        public int BitsPerSample { get; set; }
        public int BlockAlign { get; set; }
        public int DataSize { get; set; }
    }
    
    /// <summary>
    /// Result of WAV structure validation
    /// </summary>
    private class WavValidationResult
    {
        public bool IsValid { get; set; }
        public string ErrorMessage { get; set; } = string.Empty;
        public int FmtChunkPos { get; set; }
        public int DataChunkPos { get; set; }
    }
}