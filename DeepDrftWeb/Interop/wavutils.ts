interface WavHeader {
    sampleRate: number;
    channels: number;
    bitsPerSample: number;
    byteRate: number;
    blockAlign: number;
    dataSize: number;
    headerSize: number;
}

class WavUtils {
    static parseHeader(chunks: Uint8Array[], totalSize: number): WavHeader | null {
        if (totalSize < 44) return null;

        const concatenated = new Uint8Array(totalSize);
        let offset = 0;
        for (const chunk of chunks) {
            concatenated.set(chunk, offset);
            offset += chunk.length;
        }

        // Need a DataView that spans the entire buffer for chunk searching
        const view = new DataView(concatenated.buffer);

        // Check RIFF header
        const riff = new TextDecoder().decode(concatenated.slice(0, 4));
        if (riff !== 'RIFF') return null;

        const wave = new TextDecoder().decode(concatenated.slice(8, 12));
        if (wave !== 'WAVE') return null;

        // Variables to store parsed header info
        let sampleRate = 0;
        let channels = 0;
        let bitsPerSample = 0;
        let byteRate = 0;
        let blockAlign = 0;
        let dataSize = 0;
        let headerSize = 0;
        let foundFmt = false;
        let foundData = false;

        // Find fmt and data chunks
        let chunkOffset = 12;
        while (chunkOffset < totalSize - 8) {
            const chunkId = new TextDecoder().decode(concatenated.slice(chunkOffset, chunkOffset + 4));
            const chunkSize = view.getUint32(chunkOffset + 4, true);

            if (chunkId === 'fmt ') {
                // Validate minimum fmt chunk size
                if (chunkSize < 16) return null;

                const audioFormat = view.getUint16(chunkOffset + 8, true);
                // Support PCM (1) and IEEE Float (3) formats
                if (audioFormat !== 1 && audioFormat !== 3) {
                    console.warn(`Unsupported audio format: ${audioFormat} (only PCM=1 and IEEE Float=3 supported)`);
                    return null;
                }

                channels = view.getUint16(chunkOffset + 10, true);
                sampleRate = view.getUint32(chunkOffset + 12, true);
                byteRate = view.getUint32(chunkOffset + 16, true);
                blockAlign = view.getUint16(chunkOffset + 20, true);
                bitsPerSample = view.getUint16(chunkOffset + 22, true);

                // Basic validation
                if (channels < 1 || channels > 8) return null;
                if (blockAlign !== channels * (bitsPerSample / 8)) return null;

                foundFmt = true;
                console.log(`Found fmt chunk: ${bitsPerSample}-bit, ${channels}ch, ${sampleRate}Hz, format=${audioFormat}`);
            }
            else if (chunkId === 'data') {
                dataSize = chunkSize;
                headerSize = chunkOffset + 8; // Audio data starts after 'data' + size (8 bytes)
                foundData = true;
                console.log(`Found data chunk at offset ${chunkOffset}, headerSize=${headerSize}, dataSize=${dataSize}`);
            }

            // Move to next chunk with proper alignment (chunks are word-aligned)
            chunkOffset += 8 + ((chunkSize + 1) & ~1);

            // If we found both chunks, we're done
            if (foundFmt && foundData) break;
        }

        // Must have found both fmt and data chunks
        if (!foundFmt || !foundData) {
            console.warn(`WAV parsing incomplete: foundFmt=${foundFmt}, foundData=${foundData}`);
            return null;
        }

        return {
            sampleRate,
            channels,
            bitsPerSample,
            byteRate,
            blockAlign,
            dataSize,
            headerSize
        };
    }

    static createHeader(wavHeader: WavHeader, dataSize: number): Uint8Array {
        const header = new ArrayBuffer(44);
        const view = new DataView(header);
        
        // RIFF header
        view.setUint8(0, 0x52); view.setUint8(1, 0x49); view.setUint8(2, 0x46); view.setUint8(3, 0x46); // "RIFF"
        view.setUint32(4, 36 + dataSize, true); // File size
        view.setUint8(8, 0x57); view.setUint8(9, 0x41); view.setUint8(10, 0x56); view.setUint8(11, 0x45); // "WAVE"
        
        // fmt chunk
        view.setUint8(12, 0x66); view.setUint8(13, 0x6d); view.setUint8(14, 0x74); view.setUint8(15, 0x20); // "fmt "
        view.setUint32(16, 16, true); // fmt chunk size
        view.setUint16(20, 1, true); // Audio format (PCM)
        view.setUint16(22, wavHeader.channels, true);
        view.setUint32(24, wavHeader.sampleRate, true);
        view.setUint32(28, wavHeader.byteRate, true);
        view.setUint16(32, wavHeader.blockAlign, true);
        view.setUint16(34, wavHeader.bitsPerSample, true);
        
        // data chunk header
        view.setUint8(36, 0x64); view.setUint8(37, 0x61); view.setUint8(38, 0x74); view.setUint8(39, 0x61); // "data"
        view.setUint32(40, dataSize, true);
        
        return new Uint8Array(header);
    }

    static copyAudioDataDirect(chunks: Uint8Array[], targetBuffer: Uint8Array, targetOffset: number, headerSize: number, audioDataSize: number): number {
        // Clear audio data area completely to prevent contamination - KEY FIX
        for (let i = targetOffset; i < targetOffset + audioDataSize; i++) {
            targetBuffer[i] = 0;
        }
        
        // Direct copy of audio data to target buffer, skipping WAV header in first chunk only
        let targetPos = targetOffset;
        let remainingSize = audioDataSize;
        let chunkIndex = 0;
        let chunkOffset = headerSize; // Skip WAV header in first chunk
        
        while (remainingSize > 0 && chunkIndex < chunks.length) {
            const chunk = chunks[chunkIndex];
            const availableInChunk = chunk.length - chunkOffset;
            const toCopy = Math.min(availableInChunk, remainingSize);
            
            if (toCopy > 0) {
                targetBuffer.set(chunk.subarray(chunkOffset, chunkOffset + toCopy), targetPos);
                targetPos += toCopy;
                remainingSize -= toCopy;
                chunkOffset += toCopy;
            }
            
            if (chunkOffset >= chunk.length) {
                chunkIndex++;
                chunkOffset = 0; // No header to skip in subsequent chunks
            }
        }
        
        return targetPos - targetOffset; // Return actual bytes copied
    }

    static patchHeaderSizes(buffer: Uint8Array, audioDataSize: number): void {
        // Patch file size (offset 4) and data chunk size (offset 40) - little endian, 4 bytes each
        const fileSize = 36 + audioDataSize;
        buffer[4] = fileSize & 0xFF;
        buffer[5] = (fileSize >> 8) & 0xFF;
        buffer[6] = (fileSize >> 16) & 0xFF;
        buffer[7] = (fileSize >> 24) & 0xFF;
        buffer[40] = audioDataSize & 0xFF;
        buffer[41] = (audioDataSize >> 8) & 0xFF;
        buffer[42] = (audioDataSize >> 16) & 0xFF;
        buffer[43] = (audioDataSize >> 24) & 0xFF;
    }

    static getSampleAlignedChunkSize(header: WavHeader, maxChunkSize: number, availableDataSize: number): number {
        const frameSize = header.blockAlign;
        
        // Much smaller minimum for streaming - just enough for Web Audio API
        const minAudioBytes = Math.max(512, frameSize * 10); // At least 512 bytes or 10 frames
        
        // If we don't have enough data, return 0 to wait for more
        if (availableDataSize < minAudioBytes) {
            return 0;
        }
        
        // Calculate frames for the available data
        const requestedSize = Math.min(maxChunkSize, availableDataSize);
        const frames = Math.floor(requestedSize / frameSize);
        return frames * frameSize;
    }
}

export { WavHeader, WavUtils };