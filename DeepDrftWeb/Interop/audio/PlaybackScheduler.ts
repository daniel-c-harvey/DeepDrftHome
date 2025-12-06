/**
 * PlaybackScheduler - Manages AudioBuffer storage and playback scheduling.
 *
 * Single Responsibility: Store decoded buffers and schedule them for playback.
 * Supports pause/resume/seek by retaining all buffers.
 */

import { AudioContextManager } from './AudioContextManager.js';

interface ScheduledSource {
    source: AudioBufferSourceNode;
    bufferIndex: number;
    startTime: number;
    endTime: number;
}

export class PlaybackScheduler {
    private contextManager: AudioContextManager;
    private buffers: AudioBuffer[] = [];
    private scheduledSources: ScheduledSource[] = [];

    // Playback timing
    private playbackAnchorTime: number = 0;      // AudioContext time when playback started/resumed
    private playbackAnchorPosition: number = 0;  // Position in audio when playback started/resumed
    private nextBufferIndex: number = 0;         // Next buffer to schedule during live streaming
    private nextScheduleTime: number = 0;        // AudioContext time for next buffer
    private isActive_: boolean = false;          // Prevents scheduling during pause/stop

    // Callbacks
    public onPlaybackEnded: (() => void) | null = null;

    constructor(contextManager: AudioContextManager) {
        this.contextManager = contextManager;
    }

    /**
     * Add a decoded buffer to storage
     */
    addBuffer(buffer: AudioBuffer): void {
        this.buffers.push(buffer);
        console.log(`📦 Buffer[${this.buffers.length - 1}] added: ${buffer.duration.toFixed(3)}s (total: ${this.getTotalDuration().toFixed(3)}s)`);
    }

    /**
     * Get total duration of all stored buffers
     */
    getTotalDuration(): number {
        return this.buffers.reduce((sum, b) => sum + b.duration, 0);
    }

    /**
     * Get number of stored buffers
     */
    getBufferCount(): number {
        return this.buffers.length;
    }

    /**
     * Get current playback position in seconds
     */
    getCurrentPosition(): number {
        if (this.playbackAnchorTime === 0) {
            return this.playbackAnchorPosition;
        }
        const elapsed = this.contextManager.currentTime - this.playbackAnchorTime;
        return Math.min(this.playbackAnchorPosition + elapsed, this.getTotalDuration());
    }

    /**
     * Start or resume playback from a specific position
     */
    playFromPosition(position: number): void {
        this.stopAllSources();

        // Find which buffer contains this position
        let accumulatedTime = 0;
        let startBufferIndex = 0;
        let offsetInBuffer = 0;

        for (let i = 0; i < this.buffers.length; i++) {
            const bufferDuration = this.buffers[i].duration;
            if (accumulatedTime + bufferDuration > position) {
                startBufferIndex = i;
                offsetInBuffer = position - accumulatedTime;
                break;
            }
            accumulatedTime += bufferDuration;
            startBufferIndex = i + 1;
        }

        if (startBufferIndex >= this.buffers.length) {
            console.log('Position beyond available buffers');
            return;
        }

        console.log(`▶️ Playing from ${position.toFixed(3)}s: buffer[${startBufferIndex}] offset=${offsetInBuffer.toFixed(3)}s`);

        // Set timing anchors
        this.playbackAnchorPosition = position;
        this.playbackAnchorTime = this.contextManager.currentTime;
        this.nextScheduleTime = this.contextManager.currentTime + 0.01; // Small lookahead
        this.nextBufferIndex = startBufferIndex;
        this.isActive_ = true;  // Enable scheduling

        // Schedule buffers
        this.scheduleBuffersFrom(startBufferIndex, offsetInBuffer);
    }

    /**
     * Schedule newly decoded buffers during live streaming
     */
    scheduleNewBuffers(): void {
        if (this.nextBufferIndex >= this.buffers.length) {
            return; // No new buffers
        }

        if (this.nextScheduleTime === 0) {
            this.nextScheduleTime = this.contextManager.currentTime + 0.01;
        }

        this.scheduleBuffersFrom(this.nextBufferIndex, 0);
    }

    /**
     * Internal: Schedule buffers starting from a specific index
     */
    private scheduleBuffersFrom(startIndex: number, offsetInFirstBuffer: number): void {
        const lookaheadTarget = 0.5; // Schedule up to 500ms ahead
        const gainNode = this.contextManager.getGainNode();

        for (let i = startIndex; i < this.buffers.length; i++) {
            const buffer = this.buffers[i];
            const isFirstBuffer = (i === startIndex && offsetInFirstBuffer > 0);
            const offset = isFirstBuffer ? offsetInFirstBuffer : 0;
            const duration = buffer.duration - offset;

            // Create and configure source
            const source = this.contextManager.getContext().createBufferSource();
            source.buffer = buffer;
            source.connect(gainNode);

            const scheduleTime = this.nextScheduleTime;
            const endTime = scheduleTime + duration;

            // Track scheduled source
            const scheduled: ScheduledSource = {
                source,
                bufferIndex: i,
                startTime: scheduleTime,
                endTime
            };
            this.scheduledSources.push(scheduled);

            // Set up ended callback
            source.onended = () => this.handleSourceEnded(scheduled);

            // Schedule the source
            source.start(scheduleTime, offset);

            console.log(`🎵 Scheduled buffer[${i}]: ${scheduleTime.toFixed(3)}s -> ${endTime.toFixed(3)}s`);

            // Update for next buffer
            this.nextScheduleTime = endTime;
            this.nextBufferIndex = i + 1;

            // Check if we have enough lookahead
            const lookahead = this.nextScheduleTime - this.contextManager.currentTime;
            if (lookahead > lookaheadTarget) {
                console.log(`📋 Lookahead: ${(lookahead * 1000).toFixed(0)}ms buffered`);
                break;
            }
        }
    }

    /**
     * Handle a source finishing playback
     */
    private handleSourceEnded(scheduled: ScheduledSource): void {
        // Ignore if we're paused/stopped (sources fire onended when stopped)
        if (!this.isActive_) {
            return;
        }

        // Remove from scheduled list
        const index = this.scheduledSources.indexOf(scheduled);
        if (index > -1) {
            this.scheduledSources.splice(index, 1);
        }

        // Schedule more buffers if available
        if (this.nextBufferIndex < this.buffers.length) {
            this.scheduleBuffersFrom(this.nextBufferIndex, 0);
        }

        // Check if all playback has finished
        if (this.scheduledSources.length === 0 && this.nextBufferIndex >= this.buffers.length) {
            console.log('✓ Playback complete');
            this.isActive_ = false;
            this.playbackAnchorTime = 0;
            this.playbackAnchorPosition = 0;
            this.onPlaybackEnded?.();
        }
    }

    /**
     * Pause playback - saves position and stops sources
     */
    pause(): number {
        const position = this.getCurrentPosition();
        this.isActive_ = false;  // Prevent handleSourceEnded from scheduling more
        this.stopAllSources();
        this.playbackAnchorPosition = position;
        this.playbackAnchorTime = 0;
        this.nextScheduleTime = 0;
        console.log(`⏸️ Paused at ${position.toFixed(3)}s`);
        return position;
    }

    /**
     * Stop all scheduled sources
     */
    stopAllSources(): void {
        for (const scheduled of this.scheduledSources) {
            try {
                scheduled.source.stop();
            } catch {
                // Source may already be stopped
            }
        }
        this.scheduledSources = [];
    }

    /**
     * Reset to beginning (for stop)
     */
    resetToStart(): void {
        this.isActive_ = false;
        this.stopAllSources();
        this.playbackAnchorPosition = 0;
        this.playbackAnchorTime = 0;
        this.nextBufferIndex = 0;
        this.nextScheduleTime = 0;
        console.log('⏮️ Reset to start');
    }

    /**
     * Full reset - clears all buffers
     */
    clear(): void {
        this.isActive_ = false;
        this.stopAllSources();
        this.buffers = [];
        this.playbackAnchorPosition = 0;
        this.playbackAnchorTime = 0;
        this.nextBufferIndex = 0;
        this.nextScheduleTime = 0;
        console.log('🗑️ Scheduler cleared');
    }

    /**
     * Check if we have buffers
     */
    hasBuffers(): boolean {
        return this.buffers.length > 0;
    }

    /**
     * Check if we have minimum buffers for playback
     */
    hasMinimumBuffers(minCount: number): boolean {
        return this.buffers.length >= minCount;
    }

    /**
     * Check if playback is active
     */
    isActive(): boolean {
        return this.isActive_;
    }
}
