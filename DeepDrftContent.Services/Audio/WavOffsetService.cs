using System.Text;

namespace DeepDrftContent.Services.Audio;

/// <summary>
/// Service for creating WAV audio streams starting from a byte offset.
/// Synthesizes a valid WAV header for the remaining audio data.
/// </summary>
public class WavOffsetService
{
    /// <summary>
    /// WAV audio format code for linear PCM. The pipeline (AudioProcessor,
    /// WavOffsetService, and wavutils.ts) is PCM-only by design — IEEE Float
    /// (format 3) and other formats are rejected at parse time so the
    /// synthesized header here can safely assume PCM.
    /// </summary>
    public const short PcmFormat = 1;

    /// <summary>
    /// Creates a stream containing a synthesized WAV header followed by audio data from the specified offset.
    /// The returned stream is composed of a small header buffer and a non-owning slice over the input
    /// buffer — no copy of the audio payload is made.
    /// </summary>
    /// <param name="fullAudioBuffer">The complete WAV file buffer</param>
    /// <param name="byteOffset">Byte offset into the raw audio data (not including original header)</param>
    /// <returns>Stream with new WAV header + audio data from offset, or null if invalid</returns>
    public Stream? CreateOffsetStream(byte[] fullAudioBuffer, long byteOffset)
    {
        var format = ParseWavHeader(fullAudioBuffer);
        if (format == null)
            return null;

        // Validate offset is within bounds and block-aligned
        if (byteOffset < 0 || byteOffset >= format.DataSize)
            return null;

        // Align to block boundary for clean audio
        var alignedOffset = (byteOffset / format.BlockAlign) * format.BlockAlign;

        // Calculate new data size (long arithmetic — DataSize may be up to ~4 GB)
        var newDataSize = format.DataSize - alignedOffset;
        if (newDataSize <= 0)
            return null;

        // MemoryStream does not support offsets or lengths beyond int.MaxValue.
        // RF64 (>2 GB audio segments) is not supported; reject before truncating.
        var sourcePosition = format.HeaderSize + alignedOffset;
        if (sourcePosition > int.MaxValue || newDataSize > int.MaxValue)
            throw new NotSupportedException("Audio file segment exceeds 2 GB; RF64 not supported");

        var newDataSizeInt = (int)newDataSize;
        var sourcePositionInt = (int)sourcePosition;

        // Create new WAV header using the format reported by the parsed header.
        // PCM is the only format we accept (see PcmFormat / ParseWavHeader), but
        // threading format.AudioFormat through keeps the header self-consistent
        // and prevents drift if the validation contract is ever relaxed.
        var newHeader = CreateWavHeader(format, newDataSizeInt);

        // Compose: 44-byte header followed by a non-copying slice of the audio payload.
        // Wrapping the original buffer in a MemoryStream window avoids a 100MB+ copy
        // that the previous MemoryStream(capacity).Write(...) implementation forced.
        var headerStream = new MemoryStream(newHeader, writable: false);
        var dataStream = new MemoryStream(
            fullAudioBuffer,
            sourcePositionInt,
            newDataSizeInt,
            writable: false,
            publiclyVisible: false);

        return new ConcatStream(headerStream, dataStream);
    }

    /// <summary>
    /// Parses the WAV header from a buffer to extract format information.
    /// PCM-only — IEEE Float (format 3) and other non-PCM formats are rejected
    /// so downstream synthesis can safely assume PCM sample encoding.
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
        long dataSize = 0;
        int headerSize = 0;
        short audioFormat = 0;
        bool foundFmt = false;
        bool foundData = false;

        // Find fmt and data chunks
        int chunkOffset = 12;
        while (chunkOffset < buffer.Length - 8)
        {
            var chunkId = Encoding.ASCII.GetString(buffer, chunkOffset, 4);
            var chunkSize = BitConverter.ToInt32(buffer, chunkOffset + 4);

            if (chunkSize < 0)
                return null;

            if (chunkId == "fmt ")
            {
                if (chunkSize < 16)
                    return null;

                audioFormat = BitConverter.ToInt16(buffer, chunkOffset + 8);
                // PCM only. Float32 WAVs were previously accepted here but the synthesized
                // header below is PCM-shaped — accepting Float would produce a corrupt file
                // claiming PCM with Float-encoded samples. AudioProcessor also rejects
                // non-PCM at upload time so this branch is defense in depth.
                if (audioFormat != PcmFormat)
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
                // WAV stores DataSize as a 32-bit unsigned int. Read as uint to preserve
                // values above int.MaxValue (files between 2–4 GB), then widen to long.
                dataSize = (long)BitConverter.ToUInt32(buffer, chunkOffset + 4);
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
            AudioFormat: audioFormat,
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
    /// Creates a standard 44-byte WAV header. The audio format code is taken from
    /// <paramref name="format"/> rather than hardcoded so the synthesized header matches
    /// what was parsed (today always <see cref="PcmFormat"/>; see ParseWavHeader).
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
        BitConverter.GetBytes(format.AudioFormat).CopyTo(header, 20); // Audio format (from parsed header)
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
/// <param name="AudioFormat">WAV fmt-chunk audio format code (1 = PCM; the only value accepted today).</param>
public record WavFormat(
    short AudioFormat,
    int SampleRate,
    int Channels,
    int BitsPerSample,
    int ByteRate,
    int BlockAlign,
    long DataSize,
    int HeaderSize
);

/// <summary>
/// Forward-only read stream over two underlying streams concatenated end-to-end.
/// Lets us serve "[synthesized header][slice of original buffer]" without
/// allocating a single contiguous buffer for the combined payload.
/// </summary>
internal sealed class ConcatStream : Stream
{
    private readonly Stream _first;
    private readonly Stream _second;
    private readonly long _length;
    private long _position;

    public ConcatStream(Stream first, Stream second)
    {
        _first = first;
        _second = second;
        _length = first.Length + second.Length;
    }

    public override bool CanRead => true;
    public override bool CanSeek => false;
    public override bool CanWrite => false;
    public override long Length => _length;

    public override long Position
    {
        get => _position;
        set => throw new NotSupportedException();
    }

    public override int Read(byte[] buffer, int offset, int count)
    {
        var total = 0;

        // Loop over _first until it returns 0 (exhausted) or the caller's buffer
        // is full. Stream.Read is not required to fill the buffer in one call even
        // when data is available (e.g. a future non-MemoryStream _first), so we must
        // keep pulling until we get 0 before advancing to _second.
        while (count > 0 && _position < _first.Length)
        {
            var read = _first.Read(buffer, offset, count);
            if (read == 0) break;
            total += read;
            _position += read;
            offset += read;
            count -= read;
        }

        if (count > 0)
        {
            var read = _second.Read(buffer, offset, count);
            total += read;
            _position += read;
        }
        return total;
    }

    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        var total = 0;

        // Same loop contract as Read() — exhaust _first before reading _second.
        while (!buffer.IsEmpty && _position < _first.Length)
        {
            var read = await _first.ReadAsync(buffer, cancellationToken);
            if (read == 0) break;
            total += read;
            _position += read;
            buffer = buffer[read..];
        }

        if (!buffer.IsEmpty)
        {
            var read = await _second.ReadAsync(buffer, cancellationToken);
            total += read;
            _position += read;
        }
        return total;
    }

    public override void Flush() { }
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _first.Dispose();
            _second.Dispose();
        }
        base.Dispose(disposing);
    }
}
