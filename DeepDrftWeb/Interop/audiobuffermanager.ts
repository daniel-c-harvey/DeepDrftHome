/**
 * AudioBufferManager - Encapsulates all audio buffer storage and scheduling logic.
 *
 * Responsibilities:
 * - Store decoded AudioBuffers (retained for pause/resume/seek)
 * - Track playback position
 * - Schedule buffers for playback from any position
 * - Handle pause/resume without losing audio data
 */

export interface ScheduledBuffer {
    source: AudioBufferSourceNode;
    startTime: number;      // AudioContext time when this buffer starts
    duration: number;       // Duration of this buffer
    bufferIndex: number;    // Index in decodedBuffers array
}

export class AudioBufferManager {
    private decodedBuffers: AudioBuffer[] = [];
    private scheduledSources: ScheduledBuffer[] = [];
    private audioContext: AudioContext;
    private gainNode: GainNode;

    // Playback state
    private playbackStartTime: number = 0;      // AudioContext.currentTime when playback started
    private playbackStartPosition: number = 0;  // Position in audio (seconds) where playback started
    private nextScheduleIndex: number = 0;      // Next buffer index to schedule during streaming
    private nextScheduleTime: number = 0;       // AudioContext time for next buffer

    // Callbacks
    public onBufferEnded: (() => void) | null = null;
    public onAllBuffersPlayed: (() => void) | null = null;

    constructor(audioContext: AudioContext, gainNode: GainNode) {
        this.audioContext = audioContext;
        this.gainNode = gainNode;
    }

    /**
     * Add a newly decoded buffer to storage
     */
    addBuffer(buffer: AudioBuffer): void {
        this.decodedBuffers.push(buffer);
        console.log(`📦 Buffer added: index=${this.decodedBuffers.length - 1}, duration=${buffer.duration.toFixed(3)}s, total=${this.getTotalDuration().toFixed(3)}s`);
    }

    /**
     * Get total duration of all stored buffers
     */
    getTotalDuration(): number {
        return this.decodedBuffers.reduce((sum, b) => sum + b.duration, 0);
    }

    /**
     * Get number of stored buffers
     */
    getBufferCount(): number {
        return this.decodedBuffers.length;
    }

    /**
     * Get current playback position in seconds
     */
    getCurrentPosition(): number {
        if (this.playbackStartTime === 0) {
            return this.playbackStartPosition;
        }
        const elapsed = this.audioContext.currentTime - this.playbackStartTime;
        return this.playbackStartPosition + elapsed;
    }

    /**
     * Schedule playback from a specific position (used for play, resume, seek)
     */
    scheduleFromPosition(position: number): void {
        // Stop any currently scheduled sources
        this.stopAllScheduled();

        // Find which buffer contains this position
        let accumulatedTime = 0;
        let startBufferIndex = 0;
        let offsetInBuffer = 0;

        for (let i = 0; i < this.decodedBuffers.length; i++) {
            const bufferDuration = this.decodedBuffers[i].duration;
            if (accumulatedTime + bufferDuration > position) {
                startBufferIndex = i;
                offsetInBuffer = position - accumulatedTime;
                break;
            }
            accumulatedTime += bufferDuration;
            startBufferIndex = i + 1;
        }

        console.log(`🎯 Scheduling from position ${position.toFixed(3)}s: buffer[${startBufferIndex}] offset=${offsetInBuffer.toFixed(3)}s`);

        // Record playback start reference
        this.playbackStartPosition = position;
        this.playbackStartTime = this.audioContext.currentTime;
        this.nextScheduleTime = this.audioContext.currentTime + 0.01; // Small lookahead

        // Schedule buffers starting from the found position
        this.scheduleBuffersFrom(startBufferIndex, offsetInBuffer);
    }

    /**
     * Schedule pending buffers during live streaming (called when new buffers arrive)
     */
    schedulePendingBuffers(): void {
        if (this.nextScheduleIndex >= this.decodedBuffers.length) {
            return; // No new buffers to schedule
        }

        // If this is the first scheduling, initialize timing
        if (this.nextScheduleTime === 0) {
            this.nextScheduleTime = this.audioContext.currentTime + 0.01;
        }

        this.scheduleBuffersFrom(this.nextScheduleIndex, 0);
    }

    /**
     * Internal: Schedule buffers starting from a specific index
     */
    private scheduleBuffersFrom(startIndex: number, offsetInFirstBuffer: number): void {
        const lookaheadTarget = 0.5; // Schedule up to 500ms ahead

        for (let i = startIndex; i < this.decodedBuffers.length; i++) {
            const buffer = this.decodedBuffers[i];
            const isFirstBuffer = (i === startIndex && offsetInFirstBuffer > 0);
            const offset = isFirstBuffer ? offsetInFirstBuffer : 0;
            const duration = buffer.duration - offset;

            // Create and configure source
            const source = this.audioContext.createBufferSource();
            source.buffer = buffer;
            source.connect(this.gainNode);

            // Set up ended callback
            const bufferIndex = i;
            source.onended = () => this.handleBufferEnded(bufferIndex);

            // Schedule the source
            source.start(this.nextScheduleTime, offset);

            // Track the scheduled source
            this.scheduledSources.push({
                source,
                startTime: this.nextScheduleTime,
                duration,
                bufferIndex: i
            });

            console.log(`🎵 Scheduled buffer[${i}]: start=${this.nextScheduleTime.toFixed(3)}s, offset=${offset.toFixed(3)}s, duration=${duration.toFixed(3)}s`);

            // Update timing for next buffer
            this.nextScheduleTime += duration;
            this.nextScheduleIndex = i + 1;

            // Check if we have enough lookahead
            const lookahead = this.nextScheduleTime - this.audioContext.currentTime;
            if (lookahead > lookaheadTarget) {
                console.log(`📋 Sufficient lookahead: ${(lookahead * 1000).toFixed(0)}ms`);
                break;
            }
        }
    }

    /**
     * Handle a buffer finishing playback
     */
    private handleBufferEnded(bufferIndex: number): void {
        // Remove from scheduled list
        this.scheduledSources = this.scheduledSources.filter(s => s.bufferIndex !== bufferIndex);

        this.onBufferEnded?.();

        // Check if all buffers have finished
        if (this.scheduledSources.length === 0 && this.nextScheduleIndex >= this.decodedBuffers.length) {
            console.log(`✓ All buffers played`);
            this.onAllBuffersPlayed?.();
        }
    }

    /**
     * Stop all scheduled sources (for pause/stop)
     */
    stopAllScheduled(): void {
        for (const scheduled of this.scheduledSources) {
            try {
                scheduled.source.stop();
            } catch (e) {
                // Source may already be stopped
            }
        }
        this.scheduledSources = [];
        console.log(`⏹️ Stopped all scheduled sources`);
    }

    /**
     * Pause playback - saves position and stops sources
     */
    pause(): number {
        const position = this.getCurrentPosition();
        this.stopAllScheduled();
        this.playbackStartPosition = position;
        this.playbackStartTime = 0;
        console.log(`⏸️ Paused at ${position.toFixed(3)}s`);
        return position;
    }

    /**
     * Reset to beginning (for stop)
     */
    resetToStart(): void {
        this.stopAllScheduled();
        this.playbackStartPosition = 0;
        this.playbackStartTime = 0;
        this.nextScheduleIndex = 0;
        this.nextScheduleTime = 0;
        console.log(`⏮️ Reset to start`);
    }

    /**
     * Full reset - clears all buffers (for unload/new track)
     */
    clear(): void {
        this.stopAllScheduled();
        this.decodedBuffers = [];
        this.playbackStartPosition = 0;
        this.playbackStartTime = 0;
        this.nextScheduleIndex = 0;
        this.nextScheduleTime = 0;
        console.log(`🗑️ Buffer manager cleared`);
    }

    /**
     * Check if we have any buffers
     */
    hasBuffers(): boolean {
        return this.decodedBuffers.length > 0;
    }

    /**
     * Check if we have enough buffers to start playback
     */
    hasMinimumBuffers(minCount: number): boolean {
        return this.decodedBuffers.length >= minCount;
    }
}
