/**
 * AudioContextManager - Manages the Web Audio API AudioContext and GainNode.
 *
 * Single Responsibility: AudioContext lifecycle and audio routing.
 *
 * Audio chain: Source → GainNode → AnalyserNode → destination
 */

import { SpectrumAnalyzer } from './SpectrumAnalyzer.js';

export class AudioContextManager {
    private audioContext: AudioContext | null = null;
    private gainNode: GainNode | null = null;
    private spectrumAnalyzer: SpectrumAnalyzer;

    constructor() {
        this.spectrumAnalyzer = new SpectrumAnalyzer();
    }

    async initialize(sampleRate: number = 44100): Promise<void> {
        const AudioContextClass = window.AudioContext || (window as any).webkitAudioContext;
        if (!AudioContextClass) {
            throw new Error('Web Audio API not supported');
        }

        this.audioContext = new AudioContextClass({ sampleRate });
        this.gainNode = this.audioContext.createGain();

        // Initialize spectrum analyzer and insert into chain
        // Chain: Source → GainNode → AnalyserNode → destination
        const analyserNode = this.spectrumAnalyzer.initialize(this.audioContext);
        this.gainNode.connect(analyserNode);
        analyserNode.connect(this.audioContext.destination);

        console.log(`AudioContext initialized: sampleRate=${this.audioContext.sampleRate}Hz, state=${this.audioContext.state}`);
    }

    async ensureReady(): Promise<void> {
        if (!this.audioContext) {
            throw new Error('AudioContext not initialized');
        }
        if (this.audioContext.state === 'suspended') {
            console.log('🔊 Resuming AudioContext');
            await this.audioContext.resume();
            console.log(`✅ AudioContext resumed: state=${this.audioContext.state}`);
        }
    }

    async recreateWithSampleRate(sampleRate: number): Promise<void> {
        if (!this.audioContext) {
            throw new Error('AudioContext not initialized');
        }

        if (this.audioContext.sampleRate === sampleRate) {
            return; // Already correct sample rate
        }

        console.log(`🔄 Recreating AudioContext: ${this.audioContext.sampleRate}Hz -> ${sampleRate}Hz`);
        await this.audioContext.close();
        await this.initialize(sampleRate);
    }

    getContext(): AudioContext {
        if (!this.audioContext) {
            throw new Error('AudioContext not initialized');
        }
        return this.audioContext;
    }

    getGainNode(): GainNode {
        if (!this.gainNode) {
            throw new Error('GainNode not initialized');
        }
        return this.gainNode;
    }

    get currentTime(): number {
        return this.audioContext?.currentTime ?? 0;
    }

    get sampleRate(): number {
        return this.audioContext?.sampleRate ?? 0;
    }

    get state(): AudioContextState | 'uninitialized' {
        return this.audioContext?.state ?? 'uninitialized';
    }

    setVolume(volume: number): void {
        if (!this.gainNode || !this.audioContext) return;
        const clampedVolume = Math.max(0, Math.min(1, volume));
        this.gainNode.gain.setValueAtTime(clampedVolume, this.audioContext.currentTime);
    }

    getVolume(): number {
        return this.gainNode?.gain.value ?? 0;
    }

    async decodeAudioData(buffer: ArrayBuffer): Promise<AudioBuffer> {
        if (!this.audioContext) {
            throw new Error('AudioContext not initialized');
        }
        return this.audioContext.decodeAudioData(buffer);
    }

    getSpectrumAnalyzer(): SpectrumAnalyzer {
        return this.spectrumAnalyzer;
    }

    dispose(): void {
        this.spectrumAnalyzer.dispose();
        if (this.audioContext && this.audioContext.state !== 'closed') {
            this.audioContext.close();
        }
        this.audioContext = null;
        this.gainNode = null;
    }
}
