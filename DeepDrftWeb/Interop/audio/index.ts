/**
 * Audio Interop - Exposes AudioPlayer to Blazor via window.DeepDrftAudio
 */

import { AudioPlayer, AudioResult, StreamingResult, AudioState } from './AudioPlayer.js';

// Player instances by ID
const audioPlayers = new Map<string, AudioPlayer>();

// .NET interop type
interface DotNetObjectReference {
    invokeMethodAsync(methodName: string, ...args: unknown[]): Promise<unknown>;
}

// Global API exposed to Blazor
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

    initializeStreaming: (playerId: string, totalStreamLength: number): AudioResult => {
        const player = audioPlayers.get(playerId);
        if (!player) return { success: false, error: 'Player not found' };
        return player.initializeStreaming(totalStreamLength);
    },

    processStreamingChunk: async (playerId: string, chunk: Uint8Array): Promise<StreamingResult> => {
        const player = audioPlayers.get(playerId);
        if (!player) return { success: false, error: 'Player not found' };
        return player.processStreamingChunk(chunk);
    },

    startStreamingPlayback: (playerId: string): AudioResult => {
        const player = audioPlayers.get(playerId);
        if (!player) return { success: false, error: 'Player not found' };
        return player.startStreamingPlayback();
    },

    ensureAudioContextReady: async (playerId: string): Promise<AudioResult> => {
        const player = audioPlayers.get(playerId);
        if (!player) return { success: false, error: 'Player not found' };
        return player.ensureAudioContextReady();
    },

    play: (playerId: string): AudioResult => {
        const player = audioPlayers.get(playerId);
        if (!player) return { success: false, error: 'Player not found' };
        return player.play();
    },

    pause: (playerId: string): AudioResult => {
        const player = audioPlayers.get(playerId);
        if (!player) return { success: false, error: 'Player not found' };
        return player.pause();
    },

    stop: (playerId: string): AudioResult => {
        const player = audioPlayers.get(playerId);
        if (!player) return { success: false, error: 'Player not found' };
        return player.stop();
    },

    unload: (playerId: string): AudioResult => {
        const player = audioPlayers.get(playerId);
        if (!player) return { success: false, error: 'Player not found' };
        return player.unload();
    },

    seek: (playerId: string, position: number): AudioResult => {
        const player = audioPlayers.get(playerId);
        if (!player) return { success: false, error: 'Player not found' };
        return player.seek(position);
    },

    // New methods for seek-beyond-buffer support
    getBufferedDuration: (playerId: string): number => {
        const player = audioPlayers.get(playerId);
        return player?.getBufferedDuration() ?? 0;
    },

    calculateByteOffset: (playerId: string, positionSeconds: number): number => {
        const player = audioPlayers.get(playerId);
        return player?.calculateByteOffset(positionSeconds) ?? 0;
    },

    reinitializeFromOffset: (playerId: string, totalStreamLength: number, seekPosition: number): AudioResult => {
        const player = audioPlayers.get(playerId);
        if (!player) return { success: false, error: 'Player not found' };
        return player.reinitializeFromOffset(totalStreamLength, seekPosition);
    },

    setVolume: (playerId: string, volume: number): AudioResult => {
        const player = audioPlayers.get(playerId);
        if (!player) return { success: false, error: 'Player not found' };
        return player.setVolume(volume);
    },

    getCurrentTime: (playerId: string): number => {
        const player = audioPlayers.get(playerId);
        return player?.getCurrentTime() ?? 0;
    },

    getState: (playerId: string): AudioState | null => {
        const player = audioPlayers.get(playerId);
        return player?.getState() ?? null;
    },

    setOnProgressCallback: (
        playerId: string,
        dotNetRef: DotNetObjectReference,
        methodName: string
    ): AudioResult => {
        const player = audioPlayers.get(playerId);
        if (!player) return { success: false, error: 'Player not found' };

        player.setOnProgressCallback((currentTime: number) => {
            dotNetRef.invokeMethodAsync(methodName, currentTime);
        });
        return { success: true };
    },

    setOnEndCallback: (
        playerId: string,
        dotNetRef: DotNetObjectReference,
        methodName: string
    ): AudioResult => {
        const player = audioPlayers.get(playerId);
        if (!player) return { success: false, error: 'Player not found' };

        player.setOnEndCallback(() => {
            dotNetRef.invokeMethodAsync(methodName);
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
        return { success: false, error: 'Player not found' };
    },

    // Legacy compatibility - these may not be needed but kept for safety
    initializeBufferedPlayer: (_playerId: string): AudioResult => {
        return { success: true }; // No-op for streaming mode
    },

    appendAudioBlock: (_playerId: string, _audioBlock: Uint8Array): AudioResult => {
        return { success: true }; // No-op - use processStreamingChunk instead
    },

    finalizeAudioBuffer: async (_playerId: string): Promise<AudioResult & { duration?: number }> => {
        return { success: true }; // No-op for streaming mode
    }
};

// Expose to window
declare global {
    interface Window {
        DeepDrftAudio: typeof DeepDrftAudio;
    }
}

window.DeepDrftAudio = DeepDrftAudio;

export { DeepDrftAudio };
