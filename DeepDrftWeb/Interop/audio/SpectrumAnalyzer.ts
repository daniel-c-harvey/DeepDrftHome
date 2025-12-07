/**
 * SpectrumAnalyzer - Manages FFT analysis with filtering and slope correction.
 *
 * Single Responsibility: FFT analysis, frequency bucketing, and visual processing filters.
 */

export interface SpectrumConfig {
    bucketCount: number;
    highPassFreq: number;  // Hz, 0 = disabled
    lowPassFreq: number;   // Hz
    slopeDb: number;       // dB/decade correction
}

export class SpectrumAnalyzer {
    private analyser: AnalyserNode | null = null;
    private audioContext: AudioContext | null = null;
    private fftSize: number = 2048;
    private dataArray: Float32Array<ArrayBuffer> | null = null;

    // Configuration
    private bucketCount: number = 32;
    private highPassFreq: number = 0;
    private lowPassFreq: number = 20000;
    private slopeDb: number = 0;

    // Animation state - supports multiple callbacks per player
    private animationId: number | null = null;
    private callbacks = new Map<string, (data: number[]) => void>();
    private lastFrameTime: number = 0;
    private targetFrameInterval: number = 1000 / 30; // ~30fps for smooth visuals without excessive interop

    initialize(context: AudioContext): AnalyserNode {
        this.audioContext = context;
        this.analyser = context.createAnalyser();
        this.analyser.fftSize = this.fftSize;
        this.analyser.smoothingTimeConstant = 0.8;
        this.dataArray = new Float32Array(this.analyser.frequencyBinCount);

        console.log(`SpectrumAnalyzer initialized: fftSize=${this.fftSize}, bins=${this.analyser.frequencyBinCount}`);
        return this.analyser;
    }

    getAnalyserNode(): AnalyserNode | null {
        return this.analyser;
    }

    setConfig(config: Partial<SpectrumConfig>): void {
        if (config.bucketCount !== undefined) this.bucketCount = config.bucketCount;
        if (config.highPassFreq !== undefined) this.highPassFreq = config.highPassFreq;
        if (config.lowPassFreq !== undefined) this.lowPassFreq = config.lowPassFreq;
        if (config.slopeDb !== undefined) this.slopeDb = config.slopeDb;
    }

    setHighPass(freq: number): void {
        this.highPassFreq = Math.max(0, freq);
    }

    setLowPass(freq: number): void {
        this.lowPassFreq = Math.max(20, freq);
    }

    setSlopeCorrection(dbPerDecade: number): void {
        this.slopeDb = dbPerDecade;
    }

    /**
     * Get frequency data as normalized values (0-1) for each bucket
     */
    getFrequencyData(): number[] {
        if (!this.analyser || !this.dataArray || !this.audioContext) {
            return new Array(this.bucketCount).fill(0);
        }

        // Get raw FFT data (in dB, typically -100 to 0)
        this.analyser.getFloatFrequencyData(this.dataArray);

        const nyquist = this.audioContext.sampleRate / 2;
        const binCount = this.dataArray.length;
        const buckets: number[] = new Array(this.bucketCount).fill(0);

        // Logarithmic frequency mapping for perceptual balance
        // Map 20Hz - 20kHz to buckets using log scale
        const minFreq = 20;
        const maxFreq = Math.min(20000, nyquist);
        const logMin = Math.log10(minFreq);
        const logMax = Math.log10(maxFreq);
        const logRange = logMax - logMin;

        for (let bucket = 0; bucket < this.bucketCount; bucket++) {
            // Calculate frequency range for this bucket
            const logFreqLow = logMin + (bucket / this.bucketCount) * logRange;
            const logFreqHigh = logMin + ((bucket + 1) / this.bucketCount) * logRange;
            const freqLow = Math.pow(10, logFreqLow);
            const freqHigh = Math.pow(10, logFreqHigh);

            // Map frequencies to FFT bins
            const binLow = Math.floor((freqLow / nyquist) * binCount);
            const binHigh = Math.ceil((freqHigh / nyquist) * binCount);

            // Average the bins in this range
            let sum = 0;
            let count = 0;
            for (let bin = binLow; bin < binHigh && bin < binCount; bin++) {
                const freq = (bin / binCount) * nyquist;
                let value = this.dataArray[bin];

                // Apply filters
                value = this.applyFilters(value, freq);

                sum += value;
                count++;
            }

            const avgDb = count > 0 ? sum / count : -100;

            // Normalize from dB (-100 to 0) to 0-1 range
            // Clamp to reasonable range and scale
            const normalizedDb = Math.max(-80, Math.min(0, avgDb));
            buckets[bucket] = (normalizedDb + 80) / 80;
        }

        return buckets;
    }

    /**
     * Apply high-pass, low-pass, and slope correction filters
     */
    private applyFilters(valueDb: number, freq: number): number {
        // Convert dB to linear for filter math
        let linear = Math.pow(10, valueDb / 20);

        // High-pass filter (6dB/octave)
        if (this.highPassFreq > 0 && freq < this.highPassFreq && freq > 0) {
            const octaves = Math.log2(this.highPassFreq / freq);
            const attenuation = Math.pow(10, (-6 * octaves) / 20);
            linear *= attenuation;
        }

        // Low-pass filter (6dB/octave)
        if (freq > this.lowPassFreq && this.lowPassFreq > 0) {
            const octaves = Math.log2(freq / this.lowPassFreq);
            const attenuation = Math.pow(10, (-6 * octaves) / 20);
            linear *= attenuation;
        }

        // Slope correction (dB/decade, referenced to 1kHz)
        if (this.slopeDb !== 0 && freq > 0) {
            const decades = Math.log10(freq / 1000);
            const correction = Math.pow(10, (this.slopeDb * decades) / 20);
            linear *= correction;
        }

        // Convert back to dB
        return linear > 0 ? 20 * Math.log10(linear) : -100;
    }

    /**
     * Add a callback for spectrum data. Starts animation loop on first subscriber.
     */
    addCallback(id: string, callback: (data: number[]) => void): void {
        const wasEmpty = this.callbacks.size === 0;
        this.callbacks.set(id, callback);
        if (wasEmpty) {
            this.lastFrameTime = 0;
            this.animationId = requestAnimationFrame(this.animate);
        }
    }

    /**
     * Remove a callback by ID. Stops animation loop when no subscribers remain.
     */
    removeCallback(id: string): void {
        this.callbacks.delete(id);
        if (this.callbacks.size === 0) {
            this.stopAnimation();
        }
    }

    /**
     * Stop animation loop
     */
    stopAnimation(): void {
        if (this.animationId !== null) {
            cancelAnimationFrame(this.animationId);
            this.animationId = null;
        }
    }

    private animate = (timestamp: number): void => {
        if (this.callbacks.size === 0) return;

        // Throttle to target frame rate
        const elapsed = timestamp - this.lastFrameTime;
        if (elapsed >= this.targetFrameInterval) {
            this.lastFrameTime = timestamp - (elapsed % this.targetFrameInterval);
            const data = this.getFrequencyData();
            // Broadcast to all callbacks
            for (const cb of this.callbacks.values()) {
                cb(data);
            }
        }

        this.animationId = requestAnimationFrame(this.animate);
    };

    dispose(): void {
        this.stopAnimation();
        this.callbacks.clear();
        this.analyser = null;
        this.audioContext = null;
        this.dataArray = null;
    }
}
