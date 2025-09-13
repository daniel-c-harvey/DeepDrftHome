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

        const view = new DataView(concatenated.buffer, 0, 44);
        
        // Check RIFF header
        const riff = new TextDecoder().decode(concatenated.slice(0, 4));
        if (riff !== 'RIFF') return null;

        const wave = new TextDecoder().decode(concatenated.slice(8, 12));
        if (wave !== 'WAVE') return null;

        // Find fmt chunk
        let fmtOffset = 12;
        while (fmtOffset < totalSize - 8) {
            const chunkId = new TextDecoder().decode(concatenated.slice(fmtOffset, fmtOffset + 4));
            const chunkSize = view.getUint32(fmtOffset + 4, true);
            
            if (chunkId === 'fmt ') {
                const channels = view.getUint16(fmtOffset + 10, true);
                const sampleRate = view.getUint32(fmtOffset + 12, true);
                const byteRate = view.getUint32(fmtOffset + 16, true);
                const blockAlign = view.getUint16(fmtOffset + 20, true);
                const bitsPerSample = view.getUint16(fmtOffset + 22, true);
                
                return {
                    sampleRate,
                    channels,
                    bitsPerSample,
                    byteRate,
                    blockAlign,
                    dataSize: 0, // Will be updated when we find data chunk
                    headerSize: 44
                };
            }
            
            fmtOffset += 8 + chunkSize;
        }

        return null;
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

    static extractAudioData(chunks: Uint8Array[], totalSize: number, headerSize: number, chunkSize: number): Uint8Array {
        const bufferData = new Uint8Array(chunkSize + headerSize);
        let dataOffset = headerSize; // Skip header space initially
        let remainingSize = chunkSize;
        
        // Fill with audio data, skipping the header from the first chunk
        let chunkIndex = 0;
        let chunkOffset = headerSize; // Skip WAV header in first chunk
        
        while (remainingSize > 0 && chunkIndex < chunks.length) {
            const chunk = chunks[chunkIndex];
            const availableInChunk = chunk.length - chunkOffset;
            const toCopy = Math.min(availableInChunk, remainingSize);
            
            if (toCopy > 0) {
                bufferData.set(chunk.slice(chunkOffset, chunkOffset + toCopy), dataOffset);
                dataOffset += toCopy;
                remainingSize -= toCopy;
                chunkOffset += toCopy;
            }
            
            if (chunkOffset >= chunk.length) {
                chunkIndex++;
                chunkOffset = 0; // No header to skip in subsequent chunks
            }
        }

        return bufferData.slice(0, dataOffset);
    }
}

export { WavHeader, WavUtils };