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
    private expectedSize: number = 0;
    private currentSize: number = 0;
    
    // Streaming properties
    private isStreamingMode: boolean = false;
    private wavHeader: WavHeader | null = null;
    private bufferQueue: AudioBuffer[] = [];
    private currentStreamSource: AudioBufferSourceNode | null = null;
    private nextStartTime: number = 0;
    private streamingStarted: boolean = false;
    private minBuffersForStreaming: number = 3;
    
    // Buffer optimization
    private cachedWavHeader: Uint8Array | null = null;
    private reusableBuffer: Uint8Array | null = null;
    private maxReusableBufferSize: number = 128 * 1024; // 128KB max reusable buffer

    async initialize(): Promise<AudioResult> {
        try {
            const AudioContextClass = window.AudioContext || window.webkitAudioContext;
            if (!AudioContextClass) {
                throw new Error('Web Audio API not supported');
            }
            this.audioContext = new AudioContextClass();
            this.gainNode = this.audioContext.createGain();
            this.gainNode.connect(this.audioContext.destination);
            return { success: true };
        } catch (error) {
            return { success: false, error: (error as Error).message };
        }
    }

    initializeBuffered(): AudioResult {
        try {
            this.bufferChunks = [];
            this.currentSize = 0;
            this.expectedSize = 0;
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

    initializeStreaming(): AudioResult {
        try {
            this.isStreamingMode = true;
            this.bufferChunks = [];
            this.bufferQueue = [];
            this.currentSize = 0;
            this.wavHeader = null;
            this.streamingStarted = false;
            this.nextStartTime = 0;
            return { success: true };
        } catch (error) {
            return { success: false, error: (error as Error).message };
        }
    }

    processStreamingChunk(audioChunk: Uint8Array): StreamingResult {
        try {
            this.bufferChunks.push(audioChunk);
            this.currentSize += audioChunk.length;

            // Parse WAV header from first chunk if not done yet
            if (!this.wavHeader && this.currentSize >= 44) {
                const header = WavUtils.parseHeader(this.bufferChunks, this.currentSize);
                if (header) {
                    this.wavHeader = header;
                    // Cache the WAV header for reuse
                    this.cachedWavHeader = WavUtils.createHeader(header, 64 * 1024); // Cache with dummy size
                }
            }

            // Try to create audio buffers from accumulated chunks
            if (this.wavHeader) {
                this.processBufferedChunks();
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

    startStreamingPlayback(): AudioResult {
        if (!this.wavHeader || this.bufferQueue.length === 0) {
            return { success: false, error: "Not ready for streaming playback" };
        }

        try {
            if (this.audioContext!.state === 'suspended') {
                this.audioContext!.resume();
            }

            this.streamingStarted = true;
            this.isPlaying = true;
            this.isPaused = false;
            this.nextStartTime = this.audioContext!.currentTime;
            this.startTime = this.nextStartTime;
            
            this.scheduleNextBuffer();
            this.startProgressTracking();

            return { success: true };
        } catch (error) {
            return { success: false, error: (error as Error).message };
        }
    }


    private processBufferedChunks(): void {
        if (!this.wavHeader || this.bufferChunks.length === 0) return;

        try {
            // Process chunks in groups to create audio buffers
            const chunkSize = 64 * 1024; // 64KB chunks for streaming
            while (this.currentSize >= chunkSize + this.wavHeader.headerSize) {
                // Extract audio data using WavUtils
                const audioData = WavUtils.extractAudioData(this.bufferChunks, this.currentSize, this.wavHeader.headerSize, chunkSize);
                
                // Reuse buffer if possible to reduce allocations
                const totalSize = this.cachedWavHeader!.length + audioData.length - this.wavHeader.headerSize;
                if (!this.reusableBuffer || this.reusableBuffer.length < totalSize) {
                    // Only allocate if we don't have a buffer or it's too small
                    this.reusableBuffer = new Uint8Array(Math.min(totalSize, this.maxReusableBufferSize));
                }
                
                // Create complete WAV buffer using cached header and reusable buffer
                const completeBuffer = this.reusableBuffer.slice(0, totalSize);
                completeBuffer.set(this.cachedWavHeader!.slice(0, this.wavHeader.headerSize), 0);
                completeBuffer.set(audioData.subarray(this.wavHeader.headerSize), this.wavHeader.headerSize);

                // Create audio buffer from the chunk
                this.createAudioBufferFromChunk(completeBuffer);
                
                // Remove processed data
                this.removeProcessedChunks(chunkSize);
                break; // Process one chunk at a time
            }
        } catch (error) {
            console.error('Error processing buffered chunks:', error);
        }
    }

    private async createAudioBufferFromChunk(chunkData: Uint8Array): Promise<void> {
        try {
            const arrayBuffer = chunkData.buffer.slice(chunkData.byteOffset, chunkData.byteOffset + chunkData.byteLength);
            const audioBuffer = await this.audioContext!.decodeAudioData(arrayBuffer);
            this.bufferQueue.push(audioBuffer);
            
            // Schedule buffer if streaming has started
            if (this.streamingStarted) {
                this.scheduleNextBuffer();
            }
        } catch (error) {
            console.error('Error creating audio buffer from chunk:', error);
        }
    }

    private scheduleNextBuffer(): void {
        if (this.bufferQueue.length === 0 || !this.streamingStarted) return;

        const buffer = this.bufferQueue.shift()!;
        const source = this.audioContext!.createBufferSource();
        source.buffer = buffer;
        source.connect(this.gainNode!);
        
        source.onended = () => {
            if (this.bufferQueue.length > 0) {
                this.scheduleNextBuffer();
            } else if (!this.isPlaying) {
                this.onEndCallback?.();
            }
        };

        source.start(this.nextStartTime);
        this.nextStartTime += buffer.duration;
        this.currentStreamSource = source;
    }


    private removeProcessedChunks(processedSize: number): void {
        let remaining = processedSize;
        
        while (remaining > 0 && this.bufferChunks.length > 0) {
            const chunk = this.bufferChunks[0];
            if (chunk.length <= remaining) {
                remaining -= chunk.length;
                this.currentSize -= chunk.length;
                this.bufferChunks.shift();
            } else {
                // Partial chunk removal
                const newChunk = chunk.slice(remaining);
                this.bufferChunks[0] = newChunk;
                this.currentSize -= remaining;
                remaining = 0;
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
            this.expectedSize = 0;
            
            // Clean up streaming state
            this.isStreamingMode = false;
            this.wavHeader = null;
            this.bufferQueue = [];
            this.streamingStarted = false;
            this.nextStartTime = 0;
            if (this.currentStreamSource) {
                this.currentStreamSource.stop();
                this.currentStreamSource = null;
            }
            
            // Clean up cached buffers
            this.cachedWavHeader = null;
            this.reusableBuffer = null;
            
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
        this.cachedWavHeader = null;
        this.reusableBuffer = null;
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
    initializeStreaming: (playerId: string): AudioResult => {
        const player = audioPlayers.get(playerId);
        if (!player) {
            return { success: false, error: "Player not found" };
        }
        return player.initializeStreaming();
    },

    processStreamingChunk: (playerId: string, audioChunk: Uint8Array): StreamingResult => {
        const player = audioPlayers.get(playerId);
        if (!player) {
            return { success: false, error: "Player not found" };
        }
        return player.processStreamingChunk(audioChunk);
    },

    startStreamingPlayback: (playerId: string): AudioResult => {
        const player = audioPlayers.get(playerId);
        if (!player) {
            return { success: false, error: "Player not found" };
        }
        return player.startStreamingPlayback();
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