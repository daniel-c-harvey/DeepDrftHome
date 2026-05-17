/**
 * StreamDecoder - Handles WAV stream parsing and AudioBuffer decoding.
 *
 * Single Responsibility: Convert raw WAV stream data into decoded AudioBuffers.
 */

import { WavHeader, WavUtils } from '../wavutils.js';
import { AudioContextManager } from './AudioContextManager.js';

export interface DecodedChunkResult {
    buffer: AudioBuffer;
    duration: number;
}

/**
 * Thrown when decodeAudioData exceeds the per-segment deadline. Distinct from
 * DecodeError so callers (and operators reading logs) can tell a slow/throttled
 * decoder from corrupt audio data — the previous "Decode timeout" string error
 * was indistinguishable from any other Error and was silently swallowed.
 */
export class DecodeTimeoutError extends Error {
    constructor(public readonly segmentOffset: number, public readonly byteCount: number) {
        super(`Decode timeout at offset ${segmentOffset} (${byteCount} bytes)`);
        this.name = 'DecodeTimeoutError';
    }
}

/**
 * Thrown when decodeAudioData rejects for non-timeout reasons (corrupt header,
 * unsupported format, etc.). Carries the segment offset so callers can log
 * which part of the stream failed.
 */
export class DecodeError extends Error {
    constructor(
        message: string,
        public readonly segmentOffset: number,
        public readonly byteCount: number,
        public readonly cause?: Error
    ) {
        super(message);
        this.name = 'DecodeError';
    }
}

export class StreamDecoder {
    // Upper bound on pre-header accumulation. 256 KB is far beyond any sane WAV
    // header (including extended LIST/INFO/JUNK chunks). If we have accumulated
    // this many bytes without finding a valid header the stream is corrupt.
    private static readonly MAX_HEADER_SEARCH_BYTES = 256 * 1024;

    private contextManager: AudioContextManager;
    private wavHeader: WavHeader | null = null;
    private rawChunks: Uint8Array[] = [];
    private totalRawBytes: number = 0;
    private processedBytes: number = 0;
    private totalStreamLength: number = 0;
    private streamComplete: boolean = false;
    private headerError: string | null = null;

    // Pre-header accumulator. WAV headers can span multiple network chunks
    // (small first segment, extended LIST/INFO/JUNK chunks before 'data', etc.),
    // so we buffer raw bytes here until parseHeader succeeds rather than assuming
    // the whole header lives in the first chunk.
    private headerBytesReceived: number = 0;
    private headerSearchChunks: Uint8Array[] = [];

    constructor(contextManager: AudioContextManager) {
        this.contextManager = contextManager;
    }

    /**
     * Initialize for a new stream
     */
    initialize(totalStreamLength: number): void {
        this.wavHeader = null;
        this.rawChunks = [];
        this.totalRawBytes = 0;
        this.processedBytes = 0;
        this.totalStreamLength = totalStreamLength;
        this.streamComplete = false;
        this.headerBytesReceived = 0;
        this.headerSearchChunks = [];
        this.headerError = null;
        console.log(`StreamDecoder initialized: expecting ${totalStreamLength} bytes`);
    }

    /**
     * Process incoming chunk and return all decoded AudioBuffers ready so far.
     *
     * Returns an array (possibly empty) rather than a single result because the
     * final chunk may unlock the residual tail in addition to a full segment,
     * and a single chunk that completes header parsing may also carry enough
     * audio data to decode immediately.
     */
    async processChunk(chunk: Uint8Array): Promise<DecodedChunkResult[]> {
        // If the header search already failed (corrupt/non-WAV stream), stop processing.
        if (this.headerError) {
            throw new Error(this.headerError);
        }

        if (!this.wavHeader) {
            await this.tryParseHeader(chunk);
            // Check again: tryParseHeader may have just set headerError.
            if (this.headerError) {
                throw new Error(this.headerError);
            }
        } else {
            this.addRawData(chunk);
        }

        this.updateStreamCompleteFlag();

        const results: DecodedChunkResult[] = [];
        // Drain all currently-decodable segments. Without this loop, a single
        // processChunk call returns at most one segment; the trailing tail
        // unlocked once streamComplete flips true would never be flushed.
        while (true) {
            const segment = await this.tryDecodeNextSegment();
            if (!segment) break;
            results.push(segment);
        }
        return results;
    }

    /**
     * Accumulate bytes into the header-search buffer and retry parseHeader.
     * Once a header is recognised, anything past headerSize becomes audio data.
     */
    private async tryParseHeader(chunk: Uint8Array): Promise<void> {
        this.headerSearchChunks.push(chunk);
        this.headerBytesReceived += chunk.length;

        // Guard against unbounded accumulation from a corrupt or non-WAV stream.
        if (this.headerBytesReceived > StreamDecoder.MAX_HEADER_SEARCH_BYTES) {
            this.headerError = `WAV header not found after ${this.headerBytesReceived} bytes — stream may be corrupt or not a WAV file`;
            console.error(this.headerError);
            // Drop the search buffer so subsequent chunks are not accumulated either.
            this.headerSearchChunks = [];
            this.headerBytesReceived = 0;
            return;
        }

        const header = WavUtils.parseHeader(this.headerSearchChunks, this.headerBytesReceived);
        if (!header) {
            // Not enough bytes yet — wait for the next chunk. If the stream ends
            // without ever producing a valid header, the final processChunk will
            // mark streamComplete and the player will report no audio decoded;
            // that is the correct failure mode, since there is no audio to play.
            console.log(`Header not yet parsable: ${this.headerBytesReceived} bytes accumulated`);
            return;
        }

        this.wavHeader = header;
        console.log(`WAV format: ${header.bitsPerSample}-bit, ${header.channels}ch, ${header.sampleRate}Hz`);
        console.log(`Header size: ${header.headerSize}, byteRate: ${header.byteRate}`);

        // Recreate AudioContext with correct sample rate if needed
        if (this.contextManager.sampleRate !== header.sampleRate) {
            await this.contextManager.recreateWithSampleRate(header.sampleRate);
        }

        // Concatenate all header-search chunks and push the audio-data tail
        // (everything past headerSize) into the raw audio buffer.
        const concatenated = new Uint8Array(this.headerBytesReceived);
        let offset = 0;
        for (const c of this.headerSearchChunks) {
            concatenated.set(c, offset);
            offset += c.length;
        }
        const audioData = concatenated.subarray(header.headerSize);
        if (audioData.length > 0) {
            this.addRawData(audioData);
        }
        console.log(`Extracted ${audioData.length} bytes of audio data from header buffer`);

        // Header-search buffer no longer needed.
        this.headerSearchChunks = [];
        this.headerBytesReceived = 0;
    }

    /**
     * Mark the stream complete once we've received all expected bytes. The
     * computation must account for whichever stage of header parsing we're in:
     * if a header has been parsed, raw audio bytes are tracked separately;
     * otherwise pre-header bytes count toward the total.
     */
    private updateStreamCompleteFlag(): void {
        if (this.totalStreamLength <= 0) return;
        const totalReceived = this.wavHeader
            ? this.totalRawBytes + this.wavHeader.headerSize
            : this.headerBytesReceived;
        if (totalReceived >= this.totalStreamLength) {
            this.streamComplete = true;
        }
    }

    /**
     * Add raw audio data to buffer
     */
    private addRawData(data: Uint8Array): void {
        this.rawChunks.push(data);
        this.totalRawBytes += data.length;
    }

    /**
     * Try to decode the next segment of audio.
     *
     * Failure modes:
     *  - Decode timeout: retry once, then surface as DecodeTimeoutError (typed).
     *  - Other decode error (corrupt data, format mismatch): surface as DecodeError.
     * Both are thrown rather than silently swallowed — callers (processChunk /
     * markStreamComplete) decide whether to abort the stream or skip the segment.
     * processedBytes is only advanced on success so a thrown failure does not
     * silently consume the failed segment.
     */
    private async tryDecodeNextSegment(): Promise<DecodedChunkResult | null> {
        if (!this.wavHeader) return null;

        const segmentSize = 64 * 1024; // 64KB segments
        const availableBytes = this.totalRawBytes - this.processedBytes;
        // Passing streamComplete lets the aligner relax the min-frame guard
        // for the final tail; otherwise residual <512-byte tails get dropped.
        const alignedSize = WavUtils.getSampleAlignedChunkSize(
            this.wavHeader,
            segmentSize,
            availableBytes,
            this.streamComplete
        );

        if (alignedSize <= 0) return null;

        const segmentOffset = this.processedBytes;
        console.log(`\n--- Decoding segment ---`);
        console.log(`Available: ${availableBytes} bytes, aligned size: ${alignedSize} bytes`);

        const rawSegment = this.extractAlignedData(alignedSize);
        const wavFile = this.createWavFile(rawSegment);

        try {
            const buffer = await this.decodeWithRetry(wavFile, segmentOffset, alignedSize);
            // Advance only after a successful decode so a thrown timeout/decode
            // failure does not silently drop the segment.
            this.processedBytes += alignedSize;
            console.log(`✓ Decoded: ${buffer.duration.toFixed(3)}s, ${buffer.numberOfChannels}ch`);
            return { buffer, duration: buffer.duration };
        } catch (error) {
            // Re-throw typed errors so the outer drain loop in processChunk /
            // markStreamComplete sees the real failure instead of an empty array.
            // The previous silent return hid timeouts entirely.
            if (error instanceof DecodeTimeoutError || error instanceof DecodeError) {
                throw error;
            }
            // Unknown synchronous failure during decode — wrap and surface.
            throw new DecodeError(
                `Decode failed at offset ${segmentOffset} (${alignedSize} bytes): ${(error as Error).message}`,
                segmentOffset,
                alignedSize,
                error as Error);
        }
    }

    /**
     * Decode with a single retry on timeout. Web Audio's decodeAudioData is
     * occasionally flaky under tab throttling; a retry costs little and recovers
     * the common transient case without dropping the segment.
     */
    private async decodeWithRetry(
        wavData: Uint8Array,
        segmentOffset: number,
        alignedSize: number): Promise<AudioBuffer> {
        try {
            return await this.decodeWithTimeout(wavData);
        } catch (error) {
            if (!(error instanceof DecodeTimeoutError)) {
                throw new DecodeError(
                    `Decode failed at offset ${segmentOffset} (${alignedSize} bytes): ${(error as Error).message}`,
                    segmentOffset,
                    alignedSize,
                    error as Error);
            }
            console.warn(
                `Decode timeout at offset ${segmentOffset} (${alignedSize} bytes) — retrying once`);
            try {
                return await this.decodeWithTimeout(wavData);
            } catch (retryError) {
                if (retryError instanceof DecodeTimeoutError) {
                    console.error(
                        `Decode timeout after retry at offset ${segmentOffset} (${alignedSize} bytes)`);
                    throw new DecodeTimeoutError(segmentOffset, alignedSize);
                }
                throw new DecodeError(
                    `Decode failed on retry at offset ${segmentOffset} (${alignedSize} bytes): ${(retryError as Error).message}`,
                    segmentOffset,
                    alignedSize,
                    retryError as Error);
            }
        }
    }

    /**
     * Extract aligned data from raw chunks
     */
    private extractAlignedData(size: number): Uint8Array {
        const extracted = new Uint8Array(size);
        let extractedOffset = 0;
        let remaining = size;
        let streamPosition = this.processedBytes;
        let currentPos = 0;

        for (const chunk of this.rawChunks) {
            if (remaining <= 0) break;

            if (currentPos + chunk.length <= streamPosition) {
                currentPos += chunk.length;
                continue;
            }

            const chunkStartOffset = Math.max(0, streamPosition - currentPos);
            const availableInChunk = chunk.length - chunkStartOffset;
            const toCopy = Math.min(availableInChunk, remaining);

            if (toCopy > 0) {
                extracted.set(chunk.subarray(chunkStartOffset, chunkStartOffset + toCopy), extractedOffset);
                extractedOffset += toCopy;
                remaining -= toCopy;
            }

            currentPos += chunk.length;
        }

        return extracted;
    }

    /**
     * Create a complete WAV file from raw audio data
     */
    private createWavFile(rawData: Uint8Array): Uint8Array {
        const header = WavUtils.createHeader(this.wavHeader!, rawData.length);
        const wavFile = new Uint8Array(header.length + rawData.length);
        wavFile.set(header, 0);
        wavFile.set(rawData, header.length);
        return wavFile;
    }

    /**
     * Decode with timeout to prevent hanging. Throws DecodeTimeoutError if the
     * deadline expires so callers can distinguish timeout from corrupt-data
     * failures (decodeAudioData throws DOMException for the latter).
     */
    private async decodeWithTimeout(wavData: Uint8Array, timeoutMs: number = 5000): Promise<AudioBuffer> {
        const buffer = new ArrayBuffer(wavData.length);
        new Uint8Array(buffer).set(wavData);

        const decodePromise = this.contextManager.decodeAudioData(buffer);
        let timer: ReturnType<typeof setTimeout> | null = null;
        const timeoutPromise = new Promise<never>((_, reject) => {
            timer = setTimeout(() => reject(new DecodeTimeoutError(-1, wavData.length)), timeoutMs);
        });

        try {
            return await Promise.race([decodePromise, timeoutPromise]);
        } finally {
            if (timer !== null) clearTimeout(timer);
        }
    }

    /**
     * Get calculated duration from WAV header
     */
    getEstimatedDuration(): number | null {
        if (!this.wavHeader || this.wavHeader.byteRate <= 0) return null;

        const audioDataSize = this.wavHeader.dataSize > 0
            ? this.wavHeader.dataSize
            : (this.totalStreamLength - this.wavHeader.headerSize);

        return audioDataSize / this.wavHeader.byteRate;
    }

    /**
     * Check if WAV header has been parsed
     */
    get headerParsed(): boolean {
        return this.wavHeader !== null;
    }

    /**
     * Check if all stream data has been received
     */
    get isComplete(): boolean {
        return this.streamComplete;
    }

    /**
     * Get the WAV header info for byte offset calculation
     */
    getWavHeader(): WavHeader | null {
        return this.wavHeader;
    }

    /**
     * Calculate byte offset from a time position (in seconds)
     * Returns block-aligned byte offset for clean audio
     */
    calculateByteOffset(positionSeconds: number): number {
        if (!this.wavHeader || this.wavHeader.byteRate <= 0) return 0;

        const rawOffset = Math.floor(positionSeconds * this.wavHeader.byteRate);
        // Align to block boundary for clean audio
        return Math.floor(rawOffset / this.wavHeader.blockAlign) * this.wavHeader.blockAlign;
    }

    /**
     * Explicitly mark the stream as complete.
     *
     * Called by the C# streaming loop after ReadAsync returns 0 (no more data).
     * This ensures streamComplete is set even when the server omits Content-Length,
     * which prevents updateStreamCompleteFlag from ever firing via byte counting.
     * Returns all remaining decoded segments (the tail drain pass).
     *
     * If streamComplete was already true (set by updateStreamCompleteFlag during the
     * final processChunk call), the tail was already drained inside that call's
     * while(true) loop — return immediately to avoid a second drain pass that would
     * set streamingCompleted = true even if the first drain had a partial failure.
     */
    async markStreamComplete(): Promise<DecodedChunkResult[]> {
        if (this.streamComplete) {
            return [];
        }
        this.streamComplete = true;
        const results: DecodedChunkResult[] = [];
        while (true) {
            const segment = await this.tryDecodeNextSegment();
            if (!segment) break;
            results.push(segment);
        }
        return results;
    }

    /**
     * Reset decoder state
     */
    reset(): void {
        this.wavHeader = null;
        this.rawChunks = [];
        this.totalRawBytes = 0;
        this.processedBytes = 0;
        this.totalStreamLength = 0;
        this.streamComplete = false;
        this.headerBytesReceived = 0;
        this.headerSearchChunks = [];
        this.headerError = null;
    }

    /**
     * Reinitialize for offset streaming - preserves header format knowledge
     * Called when seeking beyond buffer to prepare for new stream from server
     */
    reinitializeForOffset(totalStreamLength: number): void {
        // Reset data state but we'll get a fresh header from the offset stream
        this.rawChunks = [];
        this.totalRawBytes = 0;
        this.processedBytes = 0;
        this.totalStreamLength = totalStreamLength;
        this.streamComplete = false;
        this.headerBytesReceived = 0;
        this.headerSearchChunks = [];
        this.headerError = null;
        // wavHeader will be reparsed from the new stream (server sends fresh header)
        this.wavHeader = null;
        console.log(`StreamDecoder reinitialized for offset: expecting ${totalStreamLength} bytes`);
    }
}
