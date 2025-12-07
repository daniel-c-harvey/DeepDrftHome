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

export class StreamDecoder {
    private contextManager: AudioContextManager;
    private wavHeader: WavHeader | null = null;
    private rawChunks: Uint8Array[] = [];
    private totalRawBytes: number = 0;
    private processedBytes: number = 0;
    private isFirstChunk: boolean = true;
    private totalStreamLength: number = 0;

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
        this.isFirstChunk = true;
        this.totalStreamLength = totalStreamLength;
        console.log(`StreamDecoder initialized: expecting ${totalStreamLength} bytes`);
    }

    /**
     * Process incoming chunk and return decoded AudioBuffer if ready
     */
    async processChunk(chunk: Uint8Array): Promise<DecodedChunkResult | null> {
        if (this.isFirstChunk) {
            await this.handleFirstChunk(chunk);
            this.isFirstChunk = false;
        } else {
            this.addRawData(chunk);
        }

        return this.tryDecodeNextSegment();
    }

    /**
     * Handle first chunk - extract WAV header and setup AudioContext
     */
    private async handleFirstChunk(chunk: Uint8Array): Promise<void> {
        console.log('\n--- Processing first chunk ---');

        const header = WavUtils.parseHeader([chunk], chunk.length);
        if (!header) {
            throw new Error('Invalid WAV header in first chunk');
        }

        this.wavHeader = header;
        console.log(`WAV format: ${header.bitsPerSample}-bit, ${header.channels}ch, ${header.sampleRate}Hz`);
        console.log(`Header size: ${header.headerSize}, byteRate: ${header.byteRate}`);

        // Recreate AudioContext with correct sample rate if needed
        if (this.contextManager.sampleRate !== header.sampleRate) {
            await this.contextManager.recreateWithSampleRate(header.sampleRate);
        }

        // Extract audio data (skip WAV header)
        const audioData = chunk.subarray(header.headerSize);
        this.addRawData(audioData);
        console.log(`Extracted ${audioData.length} bytes of audio data`);
    }

    /**
     * Add raw audio data to buffer
     */
    private addRawData(data: Uint8Array): void {
        this.rawChunks.push(data);
        this.totalRawBytes += data.length;
    }

    /**
     * Try to decode the next segment of audio
     */
    private async tryDecodeNextSegment(): Promise<DecodedChunkResult | null> {
        if (!this.wavHeader) return null;

        const segmentSize = 64 * 1024; // 64KB segments
        const availableBytes = this.totalRawBytes - this.processedBytes;
        const alignedSize = WavUtils.getSampleAlignedChunkSize(this.wavHeader, segmentSize, availableBytes);

        if (alignedSize <= 0) return null;

        console.log(`\n--- Decoding segment ---`);
        console.log(`Available: ${availableBytes} bytes, aligned size: ${alignedSize} bytes`);

        const rawSegment = this.extractAlignedData(alignedSize);
        const wavFile = this.createWavFile(rawSegment);

        try {
            const buffer = await this.decodeWithTimeout(wavFile);
            console.log(`✓ Decoded: ${buffer.duration.toFixed(3)}s, ${buffer.numberOfChannels}ch`);
            return { buffer, duration: buffer.duration };
        } catch (error) {
            console.error('Failed to decode segment:', error);
            return null;
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

        this.processedBytes += size;
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
     * Decode with timeout to prevent hanging
     */
    private async decodeWithTimeout(wavData: Uint8Array, timeoutMs: number = 5000): Promise<AudioBuffer> {
        const buffer = new ArrayBuffer(wavData.length);
        new Uint8Array(buffer).set(wavData);

        const decodePromise = this.contextManager.decodeAudioData(buffer);
        const timeoutPromise = new Promise<never>((_, reject) => {
            setTimeout(() => reject(new Error('Decode timeout')), timeoutMs);
        });

        return Promise.race([decodePromise, timeoutPromise]);
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
        return this.totalStreamLength > 0 && this.totalRawBytes >= (this.totalStreamLength - (this.wavHeader?.headerSize ?? 0));
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
     * Reset decoder state
     */
    reset(): void {
        this.wavHeader = null;
        this.rawChunks = [];
        this.totalRawBytes = 0;
        this.processedBytes = 0;
        this.isFirstChunk = true;
        this.totalStreamLength = 0;
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
        this.isFirstChunk = true;
        this.totalStreamLength = totalStreamLength;
        // wavHeader will be reparsed from the new stream (server sends fresh header)
        this.wavHeader = null;
        console.log(`StreamDecoder reinitialized for offset: expecting ${totalStreamLength} bytes`);
    }
}
