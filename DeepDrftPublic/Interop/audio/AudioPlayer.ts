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
    seekBeyondBuffer?: boolean;
    byteOffset?: number;
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
            // Full cleanup before starting new stream
            this.stopProgressTracking();
            this.scheduler.clear();
            this.streamDecoder.reset();
            this.resetState();

            // Initialize new stream
            this.isStreamingMode = true;
            this.streamDecoder.initialize(totalStreamLength);
            console.log(`Streaming initialized: ${totalStreamLength} bytes expected`);
            return { success: true };
        } catch (error) {
            return { success: false, error: (error as Error).message };
        }
    }

    /**
     * Signal to the decoder that the C# streaming loop has finished sending bytes.
     * This sets streamComplete=true and flushes any remaining decoded tail segments.
     * Must be called after the ReadAsync loop exits, regardless of whether
     * Content-Length was known — without it the tail-decode path is dead when
     * Content-Length is absent.
     */
    async markStreamComplete(): Promise<StreamingResult> {
        try {
            const results = await this.streamDecoder.markStreamComplete();
            if (results.length > 0) {
                for (const result of results) {
                    this.scheduler.addBuffer(result.buffer);
                }
                if (this.streamingStarted && this.isPlaying) {
                    this.scheduler.scheduleNewBuffers();
                }
            }
            this.streamingCompleted = true;
            console.log('Stream marked complete by C# signal');
            return { success: true, bufferCount: this.scheduler.getBufferCount() };
        } catch (error) {
            return { success: false, error: (error as Error).message };
        }
    }

    async processStreamingChunk(chunk: Uint8Array): Promise<StreamingResult> {
        try {
            const results = await this.streamDecoder.processChunk(chunk);

            if (results.length > 0) {
                for (const result of results) {
                    this.scheduler.addBuffer(result.buffer);
                }

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

    async startStreamingPlayback(): Promise<AudioResult> {
        if (!this.scheduler.hasBuffers()) {
            return { success: false, error: 'No buffers available' };
        }

        try {
            console.log('\n=== Starting streaming playback ===');

            // A backgrounded tab leaves AudioContext suspended. createBufferSource/start
            // against a suspended context produces no audio without throwing — the same
            // failure mode that was fixed for play() (resume path). Awaiting ensureReady()
            // here guarantees the context is running before playFromPosition schedules
            // any AudioBufferSourceNodes.
            await this.contextManager.ensureReady();

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

    async play(): Promise<AudioResult> {
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
            // Must await: a backgrounded tab leaves AudioContext suspended, and
            // createBufferSource/source.start against a suspended context produces
            // no audio without throwing. Firing ensureReady() without await meant
            // play() returned success but the user heard nothing.
            await this.contextManager.ensureReady();

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

        // Get buffered duration (accounting for playback offset)
        const bufferedDuration = this.scheduler.getTotalDuration() + this.scheduler.getPlaybackOffset();

        // Check if seeking within buffered content
        if (position <= bufferedDuration) {
            return this.seekWithinBuffer(position);
        } else {
            // Seeking beyond buffer - signal C# to fetch new stream
            return this.seekBeyondBuffer(position);
        }
    }

    /**
     * Seek within currently buffered content
     */
    private seekWithinBuffer(position: number): AudioResult {
        try {
            const wasPlaying = this.isPlaying;
            this.scheduler.stopAllSources();

            // Adjust position relative to buffer start (subtract playback offset)
            const bufferRelativePosition = position - this.scheduler.getPlaybackOffset();
            this.pausePosition = position;

            if (wasPlaying) {
                this.scheduler.playFromPosition(Math.max(0, bufferRelativePosition));
            }

            console.log(`🔍 Seeked within buffer to ${position.toFixed(3)}s (buffer-relative: ${bufferRelativePosition.toFixed(3)}s)`);
            return { success: true };
        } catch (error) {
            return { success: false, error: (error as Error).message };
        }
    }

    /**
     * Seek beyond buffered content - calculate byte offset for server request
     */
    private seekBeyondBuffer(position: number): AudioResult {
        try {
            const byteOffset = this.streamDecoder.calculateByteOffset(position);
            // 0 is a valid offset (seek to start of audio data). Only a negative result
            // indicates calculation failure — typically a missing/unparsed WAV header.
            if (byteOffset < 0) {
                return { success: false, error: 'Cannot calculate byte offset' };
            }

            console.log(`🔍 Seek beyond buffer to ${position.toFixed(3)}s requires byte offset ${byteOffset}`);

            // Signal that C# needs to request new stream from offset
            return {
                success: true,
                seekBeyondBuffer: true,
                byteOffset: byteOffset
            };
        } catch (error) {
            return { success: false, error: (error as Error).message };
        }
    }

    /**
     * Get the total buffered duration (for C# to check if seek is within buffer)
     */
    getBufferedDuration(): number {
        return this.scheduler.getTotalDuration() + this.scheduler.getPlaybackOffset();
    }

    /**
     * Calculate byte offset for a time position (for C# layer)
     */
    calculateByteOffset(positionSeconds: number): number {
        return this.streamDecoder.calculateByteOffset(positionSeconds);
    }

    /**
     * Reinitialize for offset streaming after seek-beyond-buffer
     * Called by C# after receiving new stream from server
     */
    reinitializeFromOffset(totalStreamLength: number, seekPosition: number): AudioResult {
        try {
            console.log(`\n=== Reinitializing for offset stream ===`);
            console.log(`Seek position: ${seekPosition.toFixed(3)}s, Stream length: ${totalStreamLength}`);

            // Stop current playback
            this.stopProgressTracking();
            const wasPlaying = this.isPlaying;
            this.isPlaying = false;

            // Clear buffers and set new offset
            this.scheduler.clearForSeek();
            this.scheduler.setPlaybackOffset(seekPosition);

            // Reinitialize decoder for new stream
            this.streamDecoder.reinitializeForOffset(totalStreamLength);

            // Update state
            this.pausePosition = seekPosition;
            this.streamingStarted = false; // Will restart when new buffers arrive
            this.streamingCompleted = false;

            console.log(`✅ Reinitialized for offset, was playing: ${wasPlaying}`);
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

    // ==================== Spectrum Analysis ====================

    getSpectrumData(): number[] {
        return this.contextManager.getSpectrumAnalyzer().getFrequencyData();
    }

    setSpectrumHighPass(freq: number): AudioResult {
        try {
            this.contextManager.getSpectrumAnalyzer().setHighPass(freq);
            return { success: true };
        } catch (error) {
            return { success: false, error: (error as Error).message };
        }
    }

    setSpectrumLowPass(freq: number): AudioResult {
        try {
            this.contextManager.getSpectrumAnalyzer().setLowPass(freq);
            return { success: true };
        } catch (error) {
            return { success: false, error: (error as Error).message };
        }
    }

    setSpectrumSlope(dbPerDecade: number): AudioResult {
        try {
            this.contextManager.getSpectrumAnalyzer().setSlopeCorrection(dbPerDecade);
            return { success: true };
        } catch (error) {
            return { success: false, error: (error as Error).message };
        }
    }

    startSpectrumAnimation(callbackId: string, callback: (data: number[]) => void): void {
        this.contextManager.getSpectrumAnalyzer().addCallback(callbackId, callback);
    }

    stopSpectrumAnimation(callbackId: string): void {
        this.contextManager.getSpectrumAnalyzer().removeCallback(callbackId);
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
