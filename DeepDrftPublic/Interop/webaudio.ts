/**
 * webaudio.ts - Legacy entry point for Blazor Audio Interop
 *
 * This file now delegates to the SOLID audio architecture in ./audio/
 * All functionality is provided by the new modular classes:
 * - AudioContextManager: Web Audio API context and routing
 * - StreamDecoder: WAV parsing and decoding
 * - PlaybackScheduler: Buffer storage and playback scheduling
 * - AudioPlayer: Main orchestrator
 */

// Re-export from the new SOLID architecture
export { DeepDrftAudio } from './audio/index.js';
export { AudioPlayer, AudioResult, StreamingResult, AudioState } from './audio/AudioPlayer.js';
