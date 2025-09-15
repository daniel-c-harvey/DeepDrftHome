interface AudioResult {
    success: boolean;
    error?: string;
}

interface LoadAudioResult extends AudioResult {
    duration?: number;
    sampleRate?: number;
    numberOfChannels?: number;
    loadProgress?: number;
}

import { WavHeader, WavUtils } from './wavutils.js';

interface StreamingResult extends AudioResult {
    canStartStreaming?: boolean;
    headerParsed?: boolean;
    bufferCount?: number;
}

interface AudioState {
    isPlaying: boolean;
    isPaused: boolean;
    currentTime: number;
    duration: number;
    volume: number;
    loadProgress: number;
}

type ProgressCallback = (currentTime: number) => void;
type EndCallback = () => void;
type DecodeSuccessCallback = (audioBuffer: AudioBuffer) => void;
type DecodeErrorCallback = (error: DOMException) => void;

declare global {
    interface Window {
        webkitAudioContext?: new() => AudioContext;
        DeepDrftAudio: typeof DeepDrftAudio;
    }
    
    interface AudioContext {
        decodeAudioData(audioData: ArrayBuffer | SharedArrayBuffer): Promise<AudioBuffer>;
        decodeAudioData(audioData: ArrayBuffer | SharedArrayBuffer, successCallback?: DecodeSuccessCallback, errorCallback?: DecodeErrorCallback): Promise<AudioBuffer>;
    }
}

class AudioPlayer {
    private audioContext: AudioContext | null = null;
    private audioBuffer: AudioBuffer | null = null;
    private source: AudioBufferSourceNode | null = null;
    private gainNode: GainNode | null = null;
    private isPlaying: boolean = false;
    private isPaused: boolean = false;
    private startTime: number = 0;
    private pauseOffset: number = 0;
    private duration: number = 0;
    private onProgressCallback: ProgressCallback | null = null;
    private onEndCallback: EndCallback | null = null;
    private progressInterval: number | null = null;
    private bufferChunks: Uint8Array[] = [];
    private currentSize: number = 0;
    private processedBytes: number = 0;  // Track how many bytes we've already processed

    // Streaming properties
    private isStreamingMode: boolean = false;
    private wavHeader: WavHeader | null = null;
    private bufferQueue: AudioBuffer[] = [];
    private currentStreamSource: AudioBufferSourceNode | null = null;
    private nextStartTime: number = 0;
    private streamingStarted: boolean = false;
    private streamingCompleted: boolean = false; // Track if streaming is finished
    private totalStreamLength: number = 0; // Total bytes expected in stream
    private minBuffersForStreaming: number = 6; // Increased for better buffering
    

    async initialize(): Promise<AudioResult> {
        try {
            const AudioContextClass = window.AudioContext || window.webkitAudioContext;
            if (!AudioContextClass) {
                throw new Error('Web Audio API not supported');
            }

            // Initialize with 44.1kHz for music (most common rate) to avoid recreation
            this.audioContext = new AudioContextClass({ sampleRate: 44100 });
            this.gainNode = this.audioContext.createGain();
            this.gainNode.connect(this.audioContext.destination);

            console.log(`AudioContext initialized: sampleRate=${this.audioContext.sampleRate}Hz, state=${this.audioContext.state}`);
            return { success: true };
        } catch (error) {
            return { success: false, error: (error as Error).message };
        }
    }

    async ensureAudioContextReady(): Promise<AudioResult> {
        try {
            if (this.audioContext!.state === 'suspended') {
                console.log('🔊 Resuming AudioContext on track selection (user interaction)');
                await this.audioContext!.resume();
                console.log(`✅ AudioContext resumed: state=${this.audioContext!.state}`);
            }
            return { success: true };
        } catch (error) {
            return { success: false, error: (error as Error).message };
        }
    }

    initializeBuffered(): AudioResult {
        try {
            this.bufferChunks = [];
            this.currentSize = 0;
            return { success: true };
        } catch (error) {
            return { success: false, error: (error as Error).message };
        }
    }

    appendAudioBlock(audioBlock: Uint8Array): AudioResult {
        try {
            this.bufferChunks.push(audioBlock);
            this.currentSize += audioBlock.length;
            return { success: true };
        } catch (error) {
            return { success: false, error: (error as Error).message };
        }
    }

    async finalizeAudioBuffer(): Promise<LoadAudioResult> {
        try {
            const arrayBuffer = new ArrayBuffer(this.currentSize);
            const view = new Uint8Array(arrayBuffer);
            let offset = 0;

            for (const chunk of this.bufferChunks) {
                view.set(chunk, offset);
                offset += chunk.length;
            }

            this.audioBuffer = await this.audioContext!.decodeAudioData(arrayBuffer);
            this.duration = this.audioBuffer.duration;

            this.bufferChunks = [];
            this.currentSize = 0;

            return {
                success: true,
                duration: this.duration,
                sampleRate: this.audioBuffer.sampleRate,
                numberOfChannels: this.audioBuffer.numberOfChannels,
                loadProgress: 100
            };
        } catch (error) {
            return { success: false, error: (error as Error).message };
        }
    }


    play(): AudioResult {
        if (!this.audioBuffer) {
            return { success: false, error: "No audio loaded" };
        }

        try {
            if (this.audioContext!.state === 'suspended') {
                this.audioContext!.resume();
            }

            this.source = this.audioContext!.createBufferSource();
            this.source.buffer = this.audioBuffer;
            this.source.connect(this.gainNode!);

            this.source.onended = () => {
                this.isPlaying = false;
                this.isPaused = false;
                this.startTime = 0;
                this.pauseOffset = 0;
                if (this.onEndCallback) {
                    this.onEndCallback();
                }
            };

            if (this.isPaused) {
                this.source.start(0, this.pauseOffset);
                this.startTime = this.audioContext!.currentTime - this.pauseOffset;
            } else {
                this.source.start(0);
                this.startTime = this.audioContext!.currentTime;
            }

            this.isPlaying = true;
            this.isPaused = false;
            this.startProgressTracking();

            return { success: true };
        } catch (error) {
            return { success: false, error: (error as Error).message };
        }
    }

    pause(): AudioResult {
        if (!this.isPlaying) {
            return { success: false, error: "Audio is not playing" };
        }

        try {
            this.source!.stop();
            this.pauseOffset += this.audioContext!.currentTime - this.startTime;
            this.isPlaying = false;
            this.isPaused = true;
            this.stopProgressTracking();

            return { success: true };
        } catch (error) {
            return { success: false, error: (error as Error).message };
        }
    }

    stop(): AudioResult {
        try {
            if (this.source) {
                this.source.stop();
            }
            this.isPlaying = false;
            this.isPaused = false;
            this.startTime = 0;
            this.pauseOffset = 0;
            this.stopProgressTracking();

            return { success: true };
        } catch (error) {
            return { success: false, error: (error as Error).message };
        }
    }

    seek(position: number): AudioResult {
        if (!this.audioBuffer || position < 0 || position > this.duration) {
            return { success: false, error: "Invalid seek position" };
        }

        try {
            const wasPlaying = this.isPlaying;

            if (this.isPlaying) {
                this.source!.stop();
            }

            this.pauseOffset = position;

            if (wasPlaying) {
                this.source = this.audioContext!.createBufferSource();
                this.source.buffer = this.audioBuffer;
                this.source.connect(this.gainNode!);

                this.source.onended = () => {
                    this.isPlaying = false;
                    this.isPaused = false;
                    this.startTime = 0;
                    this.pauseOffset = 0;
                    if (this.onEndCallback) {
                        this.onEndCallback();
                    }
                };

                this.source.start(0, position);
                this.startTime = this.audioContext!.currentTime - position;
            } else {
                this.isPaused = true;
            }

            return { success: true };
        } catch (error) {
            return { success: false, error: (error as Error).message };
        }
    }

    setVolume(volume: number): AudioResult {
        if (!this.gainNode) {
            return { success: false, error: "Audio not initialized" };
        }

        try {
            const clampedVolume = Math.max(0, Math.min(1, volume));
            this.gainNode.gain.setValueAtTime(clampedVolume, this.audioContext!.currentTime);
            return { success: true };
        } catch (error) {
            return { success: false, error: (error as Error).message };
        }
    }

    getCurrentTime(): number {
        if (!this.isPlaying && !this.isPaused) {
            return 0;
        }

        if (this.isPlaying) {
            return Math.min(this.pauseOffset + (this.audioContext!.currentTime - this.startTime), this.duration);
        } else {
            return this.pauseOffset;
        }
    }

    getState(): AudioState {
        return {
            isPlaying: this.isPlaying,
            isPaused: this.isPaused,
            currentTime: this.getCurrentTime(),
            duration: this.duration,
            volume: this.gainNode ? this.gainNode.gain.value : 0,
            loadProgress: 100
        };
    }

    private startProgressTracking(): void {
        this.stopProgressTracking();
        this.progressInterval = setInterval(() => {
            if (this.onProgressCallback) {
                this.onProgressCallback(this.getCurrentTime());
            }
        }, 100);
    }

    private stopProgressTracking(): void {
        if (this.progressInterval) {
            clearInterval(this.progressInterval);
            this.progressInterval = null;
        }
    }

    setOnProgressCallback(callback: ProgressCallback): void {
        this.onProgressCallback = callback;
    }

    setOnEndCallback(callback: EndCallback): void {
        this.onEndCallback = callback;
    }

    initializeStreaming(totalStreamLength: number): AudioResult {
        try {
            this.isStreamingMode = true;
            this.bufferChunks = [];
            this.bufferQueue = [];
            this.currentSize = 0;
            this.processedBytes = 0; // Reset stream position
            this.totalStreamLength = totalStreamLength; // Set total expected stream length
            this.wavHeader = null;
            this.streamingStarted = false;
            this.streamingCompleted = false; // Reset completion flag
            this.nextStartTime = 0;

            console.log(`Streaming initialized: expecting ${this.totalStreamLength} total bytes`);

            return { success: true };
        } catch (error) {
            return { success: false, error: (error as Error).message };
        }
    }

    private chunkCounter = 0;

    async processStreamingChunk(audioChunk: Uint8Array): Promise<StreamingResult> {
        try {
            this.chunkCounter++;
            console.log(`\n=== CHUNK ${this.chunkCounter} ===`);
            console.log(`Incoming chunk size: ${audioChunk.length}`);
            console.log(`Chunk preview:`, Array.from(audioChunk.slice(0, 32)).map(b => b.toString(16).padStart(2, '0')).join(' '));
            console.log(`Buffer queue length before processing: ${this.bufferQueue.length}`);

            await this.processChunk(audioChunk);

            // Check if we've received all expected data
            console.log(`Stream check: ${this.currentSize}/${this.totalStreamLength} bytes, completed=${this.streamingCompleted}`);
            if (this.totalStreamLength > 0 && this.currentSize >= this.totalStreamLength) {
                console.log(`Stream complete: received ${this.currentSize}/${this.totalStreamLength} bytes`);
                this.streamingCompleted = true;
            }

            const canStart = this.wavHeader !== null && this.bufferQueue.length >= this.minBuffersForStreaming;

            return {
                success: true,
                canStartStreaming: canStart,
                headerParsed: this.wavHeader !== null,
                bufferCount: this.bufferQueue.length
            };
        } catch (error) {
            return { success: false, error: (error as Error).message };
        }
    }


    private isFirstChunk = true;

    private async processChunk(audioChunk: Uint8Array): Promise<void> {
        if (this.isFirstChunk) {
            const audioData = await this.extractAudioFromFirstChunk(audioChunk);
            this.addToAudioStream(audioData);
            this.isFirstChunk = false;
        } else {
            // Continuation chunks are pure audio data
            this.addToAudioStream(audioChunk);
        }
        
        await this.processAudioStream();
    }

    private async extractAudioFromFirstChunk(chunkData: Uint8Array): Promise<Uint8Array> {
        console.log('\n--- EXTRACTING AUDIO FROM FIRST CHUNK ---');
        
        // Parse header and setup AudioContext
        const header = WavUtils.parseHeader([chunkData], chunkData.length);
        if (!header) {
            throw new Error('Invalid WAV header in first chunk');
        }
        
        this.wavHeader = header;
        console.log(`WAV format: ${header.bitsPerSample}-bit, ${header.channels}ch, ${header.sampleRate}Hz`);
        console.log(`Header details: blockAlign=${header.blockAlign}, byteRate=${header.byteRate}, headerSize=${header.headerSize}`);
        
        // Recreate AudioContext with correct sample rate if needed (only during initial setup)
        if (this.audioContext!.sampleRate !== header.sampleRate) {
            console.log(`🔄 AudioContext sample rate mismatch: ${this.audioContext!.sampleRate}Hz -> ${header.sampleRate}Hz`);

            // Only recreate if we haven't started playing yet AND AudioContext is already running
            if (!this.streamingStarted && !this.isPlaying && this.audioContext!.state === 'running') {
                console.log(`⚠️ Recreating AudioContext for proper sample rate matching`);
                await this.audioContext!.close();

                const AudioContextClass = window.AudioContext || window.webkitAudioContext;
                this.audioContext = new AudioContextClass({ sampleRate: header.sampleRate });

                this.gainNode = this.audioContext.createGain();
                this.gainNode.connect(this.audioContext.destination);

                console.log(`✅ AudioContext recreated: ${this.audioContext.sampleRate}Hz (should eliminate resampling artifacts)`);
            } else {
                console.log(`ℹ️ Keeping existing AudioContext - using Web Audio API sample rate conversion`);
            }
        }
        
        // Extract pure audio data (skip WAV header)
        const audioData = chunkData.subarray(header.headerSize);
        console.log(`Extracted ${audioData.length} bytes of audio data (skipped ${header.headerSize} byte header)`);
        
        return audioData;
    }

    private async ensureCorrectSampleRate(sampleRate: number): Promise<void> {
        if (this.audioContext!.sampleRate !== sampleRate) {
            console.log(`🔊 AUDIO CONTEXT CHANGE START: ${this.audioContext!.sampleRate}Hz -> ${sampleRate}Hz`);
            console.log(`⚠️  This may cause an audible pop/click!`);

            await this.audioContext!.close();
            console.log(`✅ Old AudioContext closed`);

            const AudioContextClass = window.AudioContext || window.webkitAudioContext;
            this.audioContext = new AudioContextClass({ sampleRate });
            console.log(`✅ New AudioContext created: actual=${this.audioContext.sampleRate}Hz (requested=${sampleRate}Hz)`);

            this.gainNode = this.audioContext.createGain();
            this.gainNode.connect(this.audioContext.destination);
            console.log(`🔊 AUDIO CONTEXT CHANGE COMPLETE`);
        }
    }

    private addToAudioStream(audioData: Uint8Array): void {
        this.bufferChunks.push(audioData);
        this.currentSize += audioData.length;
        console.log(`Added ${audioData.length} bytes to audio stream (total: ${this.currentSize} bytes)`);
    }

    private async processAudioStream(): Promise<void> {
        if (!this.wavHeader) return;

        // Process available data (but don't over-process during active playback)
        if (this.streamingStarted && this.bufferQueue.length >= 2) {
            console.log(`Buffer queue has cushion (${this.bufferQueue.length}), minimal processing`);
            // Still process but be less aggressive
        }

        // Create sample-aligned segments from continuous audio stream
        const maxSegmentSize = 64 * 1024; // 64KB segments to match C# chunks better
        const availableBytes = this.currentSize - this.processedBytes; // Only count unprocessed bytes
        const alignedSize = WavUtils.getSampleAlignedChunkSize(this.wavHeader, maxSegmentSize, availableBytes);

        if (alignedSize > 0) {
            console.log(`\n--- CREATING ALIGNED AUDIO SEGMENT ---`);
            console.log(`Available: ${availableBytes} bytes, requesting: ${alignedSize} bytes (frame-aligned, frame size: ${this.wavHeader.blockAlign})`);
            console.log(`Buffer queue: ${this.bufferQueue.length}, processing chunk`);

            // Extract sample-aligned segment from continuous stream
            const alignedSegment = this.extractAlignedData(alignedSize);
            const wavFile = this.createWavFromRawData(alignedSegment);

            await this.createAudioBufferFromChunk(wavFile);
            // Note: No longer removing processed data - we track position instead
        }
    }

    private extractAlignedData(alignedSize: number): Uint8Array {
        const extracted = new Uint8Array(alignedSize);
        let extractedOffset = 0;
        let remaining = alignedSize;
        let streamPosition = this.processedBytes; // Start from where we left off
        let currentPos = 0;

        for (const chunk of this.bufferChunks) {
            if (remaining <= 0) break;

            // Skip chunks that are entirely before our current stream position
            if (currentPos + chunk.length <= streamPosition) {
                currentPos += chunk.length;
                continue;
            }

            // Calculate the offset within this chunk to start extracting
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

        // Update processed bytes position
        this.processedBytes += alignedSize;
        console.log(`Extracted ${alignedSize} bytes from stream position ${streamPosition} -> ${this.processedBytes}`);

        return extracted;
    }

    private removeProcessedData(processedSize: number): void {
        let remaining = processedSize;
        
        while (remaining > 0 && this.bufferChunks.length > 0) {
            const firstChunk = this.bufferChunks[0];
            
            if (firstChunk.length <= remaining) {
                // Remove entire chunk
                remaining -= firstChunk.length;
                this.currentSize -= firstChunk.length;
                this.bufferChunks.shift();
            } else {
                // Partially remove chunk
                const newChunk = firstChunk.subarray(remaining);
                this.bufferChunks[0] = newChunk;
                this.currentSize -= remaining;
                remaining = 0;
            }
        }
    }

    private concatenateChunks(): Uint8Array {
        const totalSize = this.currentSize;
        const concatenated = new Uint8Array(totalSize);
        let offset = 0;
        
        for (const chunk of this.bufferChunks) {
            concatenated.set(chunk, offset);
            offset += chunk.length;
        }
        
        return concatenated;
    }

    private createWavFromRawData(rawData: Uint8Array): Uint8Array {
        const header = WavUtils.createHeader(this.wavHeader!, rawData.length);
        const wavFile = new Uint8Array(header.length + rawData.length);
        wavFile.set(header, 0);
        wavFile.set(rawData, header.length);

        console.log(`Created WAV: header=${header.length} bytes, data=${rawData.length} bytes, total=${wavFile.length} bytes`);
        console.log(`Expected duration: ${rawData.length / this.wavHeader!.byteRate} seconds`);

        return wavFile;
    }




    startStreamingPlayback(): AudioResult {
        if (!this.wavHeader || this.bufferQueue.length === 0) {
            return { success: false, error: "Not ready for streaming playback" };
        }

        try {
            console.log(`\n=== STARTING STREAMING PLAYBACK ===`);
            console.log(`AudioContext state: ${this.audioContext!.state}`);
            console.log(`AudioContext sample rate: ${this.audioContext!.sampleRate}Hz`);
            console.log(`Current time precision: ${this.audioContext!.currentTime.toFixed(6)}s`);
            console.log(`Queue ready: ${this.bufferQueue.length} buffers, ${this.bufferQueue.reduce((sum, b) => sum + b.duration, 0).toFixed(3)}s total`);

            // AudioContext should already be resumed during track selection

            const startTimestamp = performance.now();
            const audioContextTime = this.audioContext!.currentTime;

            this.streamingStarted = true;
            this.isPlaying = true;
            this.isPaused = false;
            this.nextStartTime = audioContextTime;
            this.startTime = this.nextStartTime;

            console.log(`▶️ Playback timing: audioContext=${audioContextTime.toFixed(6)}s, performance=${startTimestamp.toFixed(3)}ms`);
            console.log(`🎵 Initial nextStartTime set to: ${this.nextStartTime.toFixed(6)}s`);

            this.scheduleNextBuffer();
            this.startProgressTracking();

            console.log(`✅ Streaming playback started successfully`);
            console.log(`=====================================\n`);

            return { success: true };
        } catch (error) {
            console.error(`❌ Failed to start streaming playback:`, error);
            return { success: false, error: (error as Error).message };
        }
    }



    private async createAudioBufferFromChunk(chunkData: Uint8Array): Promise<void> {
        try {
            console.log(`createAudioBufferFromChunk: chunkData.length=${chunkData.length}`);
            
            // Create a clean ArrayBuffer with exact size (avoid reusable buffer issues)
            const cleanBuffer = new ArrayBuffer(chunkData.length);
            new Uint8Array(cleanBuffer).set(chunkData);
            
            console.log(`Decoding ${cleanBuffer.byteLength} bytes with Web Audio API`);
            console.log('Starting decode...');
            
            // Try with timeout to catch hanging decodes
            const decodePromise = this.audioContext!.decodeAudioData(cleanBuffer);
            const timeoutPromise = new Promise<never>((_, reject) => {
                setTimeout(() => reject(new Error('Decode timeout after 5 seconds')), 5000);
            });
            
            const audioBuffer = await Promise.race([decodePromise, timeoutPromise]);
            console.log("AFTER Promise.race - this should always appear after 5 seconds max");
            console.log(`\n--- DECODE SUCCESS ---`);
            console.log(`Buffer duration: ${audioBuffer.duration}s`);
            console.log(`Buffer channels: ${audioBuffer.numberOfChannels}`);
            console.log(`Buffer sample rate: ${audioBuffer.sampleRate}`);
            console.log(`Buffer length: ${audioBuffer.length} samples`);
            
            // Check if buffer contains actual audio data or silence/noise
            const channel0 = audioBuffer.getChannelData(0);
            const firstSamples = Array.from(channel0.slice(0, 10)).map(v => v.toFixed(4));
            const maxValue = Math.max(...Array.from(channel0).map(Math.abs));
            const avgValue = Array.from(channel0).reduce((sum, val) => sum + Math.abs(val), 0) / channel0.length;
            console.log(`First 10 samples:`, firstSamples);
            console.log(`Max amplitude: ${maxValue.toFixed(4)}`);
            console.log(`Average amplitude: ${avgValue.toFixed(4)}`);
            
            this.bufferQueue.push(audioBuffer);

            console.log(`\n=== BUFFER QUEUE UPDATE ===`);
            console.log(`✓ Added buffer: duration=${audioBuffer.duration.toFixed(6)}s, samples=${audioBuffer.length}`);
            console.log(`Queue state: ${this.bufferQueue.length} buffers (${this.bufferQueue.map(b => b.duration.toFixed(3)).join('s, ')}s)`);
            console.log(`Total queued audio: ${this.bufferQueue.reduce((sum, b) => sum + b.duration, 0).toFixed(3)}s`);
            console.log(`Streaming: started=${this.streamingStarted}, completed=${this.streamingCompleted}`);
            console.log(`Current playback time: ${this.audioContext!.currentTime.toFixed(6)}s`);

            // Schedule immediately when streaming has started (for gapless playback)
            if (this.streamingStarted) {
                console.log(`⏩ Triggering proactive schedule (streaming active)`);
                this.scheduleNextBuffer();
            } else {
                console.log(`⏸️ Not scheduling yet (streaming not started)`);
            }
            console.log(`===========================\n`);
        } catch (error) {
            console.error('Error creating audio buffer from chunk:', error);
            console.error('Failed chunk size:', chunkData.length);
            // Log first few bytes of the chunk for debugging
            const preview = Array.from(chunkData.slice(0, 16)).map(b => b.toString(16).padStart(2, '0')).join(' ');
            console.error('Chunk preview (first 16 bytes):', preview);
        }
    }

    private scheduleNextBuffer(): void {
        // Schedule all available buffers proactively instead of waiting for onended
        while (this.bufferQueue.length > 0 && this.streamingStarted) {
            const scheduleStartTime = performance.now();
            const buffer = this.bufferQueue.shift()!;
            const source = this.audioContext!.createBufferSource();
            source.buffer = buffer;
            source.connect(this.gainNode!);

            // Critical: Use precise timing for gapless playback
            const currentTime = this.audioContext!.currentTime;
            // For the very first buffer, add small lookahead to avoid startup glitches
            const startTime = this.nextStartTime > 0 ? this.nextStartTime : currentTime + 0.01;
            const schedulingDelay = currentTime - startTime;

            console.log(`🎵 Scheduling buffer: start=${startTime.toFixed(3)}s, duration=${buffer.duration.toFixed(3)}s, delay=${(schedulingDelay * 1000).toFixed(1)}ms ${schedulingDelay > 0.005 ? '⚠️' : '✓'}, queue=${this.bufferQueue.length}`);

            // Only log timing issues for debugging
            const gap = Math.abs(startTime - this.nextStartTime);
            if (gap > 0.001) {
                console.warn(`⚠️ TIMING GAP: ${(gap * 1000).toFixed(3)}ms between expected and actual start time`);
            }

            source.onended = () => {
                const endTime = this.audioContext!.currentTime;
                const expectedEndTime = startTime + buffer.duration;
                const timingError = Math.abs(endTime - expectedEndTime);

                console.log(`🏁 Buffer ended: timing error=${(timingError * 1000).toFixed(1)}ms`);

                this.currentStreamSource = null;

                // Check for end-of-stream
                if (this.bufferQueue.length === 0) {
                    if (this.streamingCompleted) {
                        console.log(`✓ End-of-stream: All buffers played at ${endTime.toFixed(3)}s (expected)`);
                    } else {
                        console.warn(`❌ Buffer underrun! Queue empty at ${endTime.toFixed(3)}s (unexpected during streaming)`);
                    }

                    if (!this.isPlaying) {
                        this.onEndCallback?.();
                    }
                }
            };


            source.start(startTime);

            // Calculate next start time with sample-perfect precision
            this.nextStartTime = startTime + buffer.duration;
            this.currentStreamSource = source;

            const scheduleEndTime = performance.now();
            const scheduleProcessingTime = scheduleEndTime - scheduleStartTime;


            // Stop scheduling when we have enough buffered ahead
            const lookaheadTime = this.nextStartTime - currentTime;
            if (lookaheadTime > 0.5) { // Stop when we have 500ms of audio scheduled ahead
                console.log(`📋 Sufficient lookahead: ${(lookaheadTime * 1000).toFixed(0)}ms scheduled ahead`);
                break;
            }
        }
    }



    unload(): AudioResult {
        try {
            this.stop();
            this.audioBuffer = null;
            this.duration = 0;
            this.bufferChunks = [];
            this.currentSize = 0;
            this.processedBytes = 0; // Reset stream position

            // Clean up streaming state
            this.isStreamingMode = false;
            this.wavHeader = null;
            this.bufferQueue = [];
            this.streamingStarted = false;
            this.streamingCompleted = false;
            this.totalStreamLength = 0;
            this.nextStartTime = 0;
            if (this.currentStreamSource) {
                this.currentStreamSource.stop();
                this.currentStreamSource = null;
            }
            
            
            return { success: true };
        } catch (error) {
            return { success: false, error: (error as Error).message };
        }
    }

    dispose(): void {
        this.stop();
        this.stopProgressTracking();
        if (this.audioContext && this.audioContext.state !== 'closed') {
            this.audioContext.close();
        }
        this.audioContext = null;
        this.audioBuffer = null;
        this.gainNode = null;
        this.bufferChunks = [];
        this.currentSize = 0;
        
        // Clean up streaming state
        this.bufferQueue = [];
        this.wavHeader = null;
        this.currentStreamSource = null;
    }
}

// Global player instances
const audioPlayers = new Map<string, AudioPlayer>();

// Define .NET interop types
interface DotNetObjectReference {
    invokeMethodAsync(methodName: string, ...args: any[]): Promise<any>;
}

// JavaScript interop functions for Blazor
const DeepDrftAudio = {
    createPlayer: async (playerId: string): Promise<AudioResult> => {
        try {
            const player = new AudioPlayer();
            const result = await player.initialize();
            if (result.success) {
                audioPlayers.set(playerId, player);
            }
            return result;
        } catch (error) {
            return { success: false, error: (error as Error).message };
        }
    },

    initializeBufferedPlayer: (playerId: string): AudioResult => {
        const player = audioPlayers.get(playerId);
        if (!player) {
            return { success: false, error: "Player not found" };
        }
        return player.initializeBuffered();
    },

    appendAudioBlock: (playerId: string, audioBlock: Uint8Array): AudioResult => {
        const player = audioPlayers.get(playerId);
        if (!player) {
            return { success: false, error: "Player not found" };
        }
        return player.appendAudioBlock(audioBlock);
    },

    finalizeAudioBuffer: async (playerId: string): Promise<LoadAudioResult> => {
        const player = audioPlayers.get(playerId);
        if (!player) {
            return { success: false, error: "Player not found" };
        }
        return await player.finalizeAudioBuffer();
    },

    // Streaming methods
    initializeStreaming: (playerId: string, totalStreamLength: number): AudioResult => {
        const player = audioPlayers.get(playerId);
        if (!player) {
            return { success: false, error: "Player not found" };
        }
        return player.initializeStreaming(totalStreamLength);
    },

    processStreamingChunk: async (playerId: string, audioChunk: Uint8Array): Promise<StreamingResult> => {
        const player = audioPlayers.get(playerId);
        if (!player) {
            return { success: false, error: "Player not found" };
        }
        return await player.processStreamingChunk(audioChunk);
    },

    startStreamingPlayback: (playerId: string): AudioResult => {
        const player = audioPlayers.get(playerId);
        if (!player) {
            return { success: false, error: "Player not found" };
        }
        return player.startStreamingPlayback();
    },

    ensureAudioContextReady: async (playerId: string): Promise<AudioResult> => {
        const player = audioPlayers.get(playerId);
        if (!player) {
            return { success: false, error: "Player not found" };
        }
        return await player.ensureAudioContextReady();
    },

    play: (playerId: string): AudioResult => {
        const player = audioPlayers.get(playerId);
        if (!player) {
            return { success: false, error: "Player not found" };
        }
        return player.play();
    },

    pause: (playerId: string): AudioResult => {
        const player = audioPlayers.get(playerId);
        if (!player) {
            return { success: false, error: "Player not found" };
        }
        return player.pause();
    },

    stop: (playerId: string): AudioResult => {
        const player = audioPlayers.get(playerId);
        if (!player) {
            return { success: false, error: "Player not found" };
        }
        return player.stop();
    },

    unload: (playerId: string): AudioResult => {
        const player = audioPlayers.get(playerId);
        if (!player) {
            return { success: false, error: "Player not found" };
        }
        return player.unload();
    },

    seek: (playerId: string, position: number): AudioResult => {
        const player = audioPlayers.get(playerId);
        if (!player) {
            return { success: false, error: "Player not found" };
        }
        return player.seek(position);
    },

    setVolume: (playerId: string, volume: number): AudioResult => {
        const player = audioPlayers.get(playerId);
        if (!player) {
            return { success: false, error: "Player not found" };
        }
        return player.setVolume(volume);
    },

    getCurrentTime: (playerId: string): number => {
        const player = audioPlayers.get(playerId);
        if (!player) {
            return 0;
        }
        return player.getCurrentTime();
    },

    getState: (playerId: string): AudioState | null => {
        const player = audioPlayers.get(playerId);
        if (!player) {
            return null;
        }
        return player.getState();
    },

    setOnProgressCallback: (playerId: string, dotNetObjectReference: DotNetObjectReference, methodName: string): AudioResult => {
        const player = audioPlayers.get(playerId);
        if (!player) {
            return { success: false, error: "Player not found" };
        }

        player.setOnProgressCallback((currentTime: number) => {
            dotNetObjectReference.invokeMethodAsync(methodName, currentTime);
        });

        return { success: true };
    },

    setOnEndCallback: (playerId: string, dotNetObjectReference: DotNetObjectReference, methodName: string): AudioResult => {
        const player = audioPlayers.get(playerId);
        if (!player) {
            return { success: false, error: "Player not found" };
        }

        player.setOnEndCallback(() => {
            dotNetObjectReference.invokeMethodAsync(methodName);
        });

        return { success: true };
    },

    disposePlayer: (playerId: string): AudioResult => {
        const player = audioPlayers.get(playerId);
        if (player) {
            player.dispose();
            audioPlayers.delete(playerId);
            return { success: true };
        }
        return { success: false, error: "Player not found" };
    }
};

// Assign to window for global access
window.DeepDrftAudio = DeepDrftAudio;