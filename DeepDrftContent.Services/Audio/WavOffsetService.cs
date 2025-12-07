using System.Text;

namespace DeepDrftContent.Services.Audio;

/// <summary>
/// Service for creating WAV audio streams starting from a byte offset.
/// Synthesizes a valid WAV header for the remaining audio data.
/// </summary>
public class WavOffsetService
{
    /// <summary>
    /// Creates a stream containing a synthesized WAV header followed by audio data from the specified offset.
    /// </summary>
    /// <param name="fullAudioBuffer">The complete WAV file buffer</param>
    /// <param name="byteOffset">Byte offset into the raw audio data (not including original header)</param>
    /// <returns>MemoryStream with new WAV header + audio data from offset, or null if invalid</returns>
    public MemoryStream? CreateOffsetStream(byte[] fullAudioBuffer, long byteOffset)
    {
        var format = ParseWavHeader(fullAudioBuffer);
        if (format == null)
            return null;

        // Validate offset is within bounds and block-aligned
        if (byteOffset < 0 || byteOffset >= format.DataSize)
            return null;

        // Align to block boundary for clean audio
        var alignedOffset = (byteOffset / format.BlockAlign) * format.BlockAlign;

        // Calculate new data size
        var newDataSize = format.DataSize - (int)alignedOffset;
        if (newDataSize <= 0)
            return null;

        // Create new WAV header
        var newHeader = CreateWavHeader(format, newDataSize);

        // Calculate source position in original buffer
        var sourcePosition = format.HeaderSize + alignedOffset;

        // Create result stream: new header + audio data from offset
        var resultStream = new MemoryStream(44 + newDataSize);
        resultStream.Write(newHeader, 0, 44);
        resultStream.Write(fullAudioBuffer, (int)sourcePosition, newDataSize);
        resultStream.Position = 0;

        return resultStream;
    }

    /// <summary>
    /// Parses the WAV header from a buffer to extract format information.
    /// </summary>
    public WavFormat? ParseWavHeader(byte[] buffer)
    {
        if (buffer.Length < 44)
            return null;

        // Check RIFF header
        var riff = Encoding.ASCII.GetString(buffer, 0, 4);
        if (riff != "RIFF")
            return null;

        var wave = Encoding.ASCII.GetString(buffer, 8, 4);
        if (wave != "WAVE")
            return null;

        // Variables to store parsed header info
        int sampleRate = 0;
        int channels = 0;
        int bitsPerSample = 0;
        int byteRate = 0;
        int blockAlign = 0;
        int dataSize = 0;
        int headerSize = 0;
        bool foundFmt = false;
        bool foundData = false;

        // Find fmt and data chunks
        int chunkOffset = 12;
        while (chunkOffset < buffer.Length - 8)
        {
            var chunkId = Encoding.ASCII.GetString(buffer, chunkOffset, 4);
            var chunkSize = BitConverter.ToInt32(buffer, chunkOffset + 4);

            if (chunkId == "fmt ")
            {
                if (chunkSize < 16)
                    return null;

                var audioFormat = BitConverter.ToInt16(buffer, chunkOffset + 8);
                // Support PCM (1) and IEEE Float (3) formats
                if (audioFormat != 1 && audioFormat != 3)
                    return null;

                channels = BitConverter.ToInt16(buffer, chunkOffset + 10);
                sampleRate = BitConverter.ToInt32(buffer, chunkOffset + 12);
                byteRate = BitConverter.ToInt32(buffer, chunkOffset + 16);
                blockAlign = BitConverter.ToInt16(buffer, chunkOffset + 20);
                bitsPerSample = BitConverter.ToInt16(buffer, chunkOffset + 22);

                // Basic validation
                if (channels < 1 || channels > 8)
                    return null;

                foundFmt = true;
            }
            else if (chunkId == "data")
            {
                dataSize = chunkSize;
                headerSize = chunkOffset + 8; // Audio data starts after 'data' + size (8 bytes)
                foundData = true;
            }

            // Move to next chunk with proper alignment (chunks are word-aligned)
            chunkOffset += 8 + ((chunkSize + 1) & ~1);

            // If we found both chunks, we're done
            if (foundFmt && foundData)
                break;
        }

        // Must have found both fmt and data chunks
        if (!foundFmt || !foundData)
            return null;

        return new WavFormat(
            SampleRate: sampleRate,
            Channels: channels,
            BitsPerSample: bitsPerSample,
            ByteRate: byteRate,
            BlockAlign: blockAlign,
            DataSize: dataSize,
            HeaderSize: headerSize
        );
    }

    /// <summary>
    /// Creates a standard 44-byte PCM WAV header.
    /// </summary>
    public byte[] CreateWavHeader(WavFormat format, int dataSize)
    {
        var header = new byte[44];
        var fileSize = 36 + dataSize;

        // RIFF header
        header[0] = (byte)'R'; header[1] = (byte)'I'; header[2] = (byte)'F'; header[3] = (byte)'F';
        BitConverter.GetBytes(fileSize).CopyTo(header, 4);
        header[8] = (byte)'W'; header[9] = (byte)'A'; header[10] = (byte)'V'; header[11] = (byte)'E';

        // fmt chunk
        header[12] = (byte)'f'; header[13] = (byte)'m'; header[14] = (byte)'t'; header[15] = (byte)' ';
        BitConverter.GetBytes(16).CopyTo(header, 16); // fmt chunk size
        BitConverter.GetBytes((short)1).CopyTo(header, 20); // Audio format (PCM)
        BitConverter.GetBytes((short)format.Channels).CopyTo(header, 22);
        BitConverter.GetBytes(format.SampleRate).CopyTo(header, 24);
        BitConverter.GetBytes(format.ByteRate).CopyTo(header, 28);
        BitConverter.GetBytes((short)format.BlockAlign).CopyTo(header, 32);
        BitConverter.GetBytes((short)format.BitsPerSample).CopyTo(header, 34);

        // data chunk header
        header[36] = (byte)'d'; header[37] = (byte)'a'; header[38] = (byte)'t'; header[39] = (byte)'a';
        BitConverter.GetBytes(dataSize).CopyTo(header, 40);

        return header;
    }
}

/// <summary>
/// WAV format information extracted from header.
/// </summary>
public record WavFormat(
    int SampleRate,
    int Channels,
    int BitsPerSample,
    int ByteRate,
    int BlockAlign,
    int DataSize,
    int HeaderSize
);
