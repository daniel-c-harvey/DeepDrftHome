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
type LoadProgressCallback = (progress: number) => void;

interface Window {
    webkitAudioContext?: typeof AudioContext;
    DeepDrftAudio: typeof DeepDrftAudio;
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
    private onLoadProgressCallback: LoadProgressCallback | null = null;
    private progressInterval: number | null = null;
    private bufferChunks: Uint8Array[] = [];
    private expectedSize: number = 0;
    private currentSize: number = 0;

    async initialize(): Promise<AudioResult> {
        try {
            this.audioContext = new (window.AudioContext || window.webkitAudioContext)();
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
            
            if (this.expectedSize > 0 && this.onLoadProgressCallback) {
                const progress = (this.currentSize / this.expectedSize) * 100;
                this.onLoadProgressCallback(Math.min(progress, 100));
            }
            
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
            
            if (this.onLoadProgressCallback) {
                this.onLoadProgressCallback(100);
            }
            
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

    setOnLoadProgressCallback(callback: LoadProgressCallback): void {
        this.onLoadProgressCallback = callback;
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

    setOnLoadProgressCallback: (playerId: string, dotNetObjectReference: DotNetObjectReference, methodName: string): AudioResult => {
        const player = audioPlayers.get(playerId);
        if (!player) {
            return { success: false, error: "Player not found" };
        }
        
        player.setOnLoadProgressCallback((progress: number) => {
            dotNetObjectReference.invokeMethodAsync(methodName, progress);
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