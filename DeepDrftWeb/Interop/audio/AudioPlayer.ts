/**
 * AudioPlayer - Main orchestrator for audio playback.
 *
 * Composes specialized managers following Single Responsibility Principle:
 * - AudioContextManager: Web Audio API context and routing
 * - StreamDecoder: WAV parsing and decoding
 * - PlaybackScheduler: Buffer storage and playback scheduling
 */

import { AudioContextManager } from './AudioContextManager.js';
import { StreamDecoder } from './StreamDecoder.js';
import { PlaybackScheduler } from './PlaybackScheduler.js';

export interface AudioResult {
    success: boolean;
    error?: string;
}

export interface StreamingResult extends AudioResult {
    canStartStreaming?: boolean;
    headerParsed?: boolean;
    bufferCount?: number;
    duration?: number;
}

export interface AudioState {
    isPlaying: boolean;
    isPaused: boolean;
    currentTime: number;
    duration: number;
    volume: number;
}

type ProgressCallback = (currentTime: number) => void;
type EndCallback = () => void;

export class AudioPlayer {
    private contextManager: AudioContextManager;
    private streamDecoder: StreamDecoder;
    private scheduler: PlaybackScheduler;

    // Playback state
    private isPlaying: boolean = false;
    private isPaused: boolean = false;
    private pausePosition: number = 0;
    private duration: number = 0;

    // Streaming state
    private isStreamingMode: boolean = false;
    private streamingStarted: boolean = false;
    private streamingCompleted: boolean = false;
    private minBuffersForPlayback: number = 6;

    // Callbacks
    private onProgressCallback: ProgressCallback | null = null;
    private onEndCallback: EndCallback | null = null;
    private progressInterval: number | null = null;

    constructor() {
        this.contextManager = new AudioContextManager();
        this.streamDecoder = new StreamDecoder(this.contextManager);
        this.scheduler = new PlaybackScheduler(this.contextManager);

        // Wire up scheduler callbacks
        this.scheduler.onPlaybackEnded = () => this.handlePlaybackEnded();
    }

    // ==================== Initialization ====================

    async initialize(): Promise<AudioResult> {
        try {
            await this.contextManager.initialize();
            return { success: true };
        } catch (error) {
            return { success: false, error: (error as Error).message };
        }
    }

    async ensureAudioContextReady(): Promise<AudioResult> {
        try {
            await this.contextManager.ensureReady();
            return { success: true };
        } catch (error) {
            return { success: false, error: (error as Error).message };
        }
    }

    // ==================== Streaming ====================

    initializeStreaming(totalStreamLength: number): AudioResult {
        try {
            this.resetState();
            this.isStreamingMode = true;
            this.streamDecoder.initialize(totalStreamLength);
            console.log(`Streaming initialized: ${totalStreamLength} bytes expected`);
            return { success: true };
        } catch (error) {
            return { success: false, error: (error as Error).message };
        }
    }

    async processStreamingChunk(chunk: Uint8Array): Promise<StreamingResult> {
        try {
            const result = await this.streamDecoder.processChunk(chunk);

            if (result) {
                this.scheduler.addBuffer(result.buffer);

                // Update duration estimate
                const estimatedDuration = this.streamDecoder.getEstimatedDuration();
                if (estimatedDuration) {
                    this.duration = estimatedDuration;
                }

                // Schedule new buffers if already playing
                if (this.streamingStarted && this.isPlaying) {
                    this.scheduler.scheduleNewBuffers();
                }
            }

            // Check if streaming is complete
            if (this.streamDecoder.isComplete) {
                this.streamingCompleted = true;
                console.log('Stream complete');
            }

            const canStart = this.streamDecoder.headerParsed &&
                this.scheduler.hasMinimumBuffers(this.minBuffersForPlayback);

            return {
                success: true,
                canStartStreaming: canStart,
                headerParsed: this.streamDecoder.headerParsed,
                bufferCount: this.scheduler.getBufferCount(),
                duration: this.duration
            };
        } catch (error) {
            return { success: false, error: (error as Error).message };
        }
    }

    startStreamingPlayback(): AudioResult {
        if (!this.scheduler.hasBuffers()) {
            return { success: false, error: 'No buffers available' };
        }

        try {
            console.log('\n=== Starting streaming playback ===');
            this.streamingStarted = true;
            this.isPlaying = true;
            this.isPaused = false;
            this.pausePosition = 0;

            this.scheduler.playFromPosition(0);
            this.startProgressTracking();

            console.log('✅ Streaming playback started');
            return { success: true };
        } catch (error) {
            return { success: false, error: (error as Error).message };
        }
    }

    // ==================== Playback Control ====================

    play(): AudioResult {
        if (!this.isStreamingMode) {
            return { success: false, error: 'Not in streaming mode' };
        }

        if (!this.streamingStarted || !this.scheduler.hasBuffers()) {
            return { success: false, error: 'Streaming not ready' };
        }

        // Don't restart if already playing
        if (this.isPlaying) {
            console.log('Already playing, ignoring play()');
            return { success: true };
        }

        try {
            this.contextManager.ensureReady();

            this.isPlaying = true;
            this.isPaused = false;

            // Resume from pause position
            this.scheduler.playFromPosition(this.pausePosition);
            this.startProgressTracking();

            console.log(`▶️ Resumed from ${this.pausePosition.toFixed(3)}s`);
            return { success: true };
        } catch (error) {
            return { success: false, error: (error as Error).message };
        }
    }

    pause(): AudioResult {
        if (!this.isPlaying) {
            return { success: false, error: 'Not playing' };
        }

        try {
            this.pausePosition = this.scheduler.pause();
            this.isPlaying = false;
            this.isPaused = true;
            this.stopProgressTracking();

            console.log(`⏸️ Paused at ${this.pausePosition.toFixed(3)}s`);
            return { success: true };
        } catch (error) {
            return { success: false, error: (error as Error).message };
        }
    }

    stop(): AudioResult {
        try {
            this.scheduler.clear();
            this.streamDecoder.reset();
            this.resetState();
            this.stopProgressTracking();

            console.log('⏹️ Stopped');
            return { success: true };
        } catch (error) {
            return { success: false, error: (error as Error).message };
        }
    }

    unload(): AudioResult {
        return this.stop();
    }

    seek(position: number): AudioResult {
        if (!this.isStreamingMode || position < 0 || position > this.duration) {
            return { success: false, error: 'Invalid seek position' };
        }

        try {
            const wasPlaying = this.isPlaying;
            this.scheduler.stopAllSources();
            this.pausePosition = position;

            if (wasPlaying) {
                this.scheduler.playFromPosition(position);
            }

            console.log(`🔍 Seeked to ${position.toFixed(3)}s`);
            return { success: true };
        } catch (error) {
            return { success: false, error: (error as Error).message };
        }
    }

    // ==================== Volume ====================

    setVolume(volume: number): AudioResult {
        try {
            this.contextManager.setVolume(volume);
            return { success: true };
        } catch (error) {
            return { success: false, error: (error as Error).message };
        }
    }

    // ==================== State ====================

    getCurrentTime(): number {
        if (this.isPlaying) {
            return this.scheduler.getCurrentPosition();
        }
        return this.pausePosition;
    }

    getState(): AudioState {
        return {
            isPlaying: this.isPlaying,
            isPaused: this.isPaused,
            currentTime: this.getCurrentTime(),
            duration: this.duration,
            volume: this.contextManager.getVolume()
        };
    }

    // ==================== Callbacks ====================

    setOnProgressCallback(callback: ProgressCallback): void {
        this.onProgressCallback = callback;
    }

    setOnEndCallback(callback: EndCallback): void {
        this.onEndCallback = callback;
    }

    // ==================== Private Methods ====================

    private resetState(): void {
        this.isPlaying = false;
        this.isPaused = false;
        this.pausePosition = 0;
        this.duration = 0;
        this.isStreamingMode = false;
        this.streamingStarted = false;
        this.streamingCompleted = false;
    }

    private handlePlaybackEnded(): void {
        this.isPlaying = false;
        this.isPaused = false;
        this.pausePosition = 0;
        this.stopProgressTracking();
        this.onEndCallback?.();
    }

    private startProgressTracking(): void {
        this.stopProgressTracking();
        this.progressInterval = window.setInterval(() => {
            if (this.onProgressCallback && this.isPlaying) {
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

    // ==================== Cleanup ====================

    dispose(): void {
        this.stop();
        this.stopProgressTracking();
        this.contextManager.dispose();
    }
}
