using Microsoft.JSInterop;

namespace DeepDrftWeb.Client.Services;

public class AudioInteropService : IAsyncDisposable
{
    private readonly IJSRuntime _jsRuntime;
    private readonly Dictionary<string, DotNetObjectReference<AudioPlayerCallback>> _callbacks = new();

    public AudioInteropService(IJSRuntime jsRuntime)
    {
        _jsRuntime = jsRuntime;
    }

    public async Task<AudioOperationResult> CreatePlayerAsync(string playerId)
    {
        try
        {
            var result = await _jsRuntime.InvokeAsync<AudioOperationResult>("DeepDrftAudio.createPlayer", playerId);
            return result;
        }
        catch (Exception ex)
        {
            return new AudioOperationResult { Success = false, Error = ex.Message };
        }
    }

    public async Task<AudioOperationResult> InitializeBufferedPlayerAsync(string playerId)
    {
        return await InvokeJsAsync<AudioOperationResult>("DeepDrftAudio.initializeBufferedPlayer", playerId);
    }

    public async Task<AudioOperationResult> AppendAudioBlockAsync(string playerId, byte[] audioBlock)
    {
        return await InvokeJsAsync<AudioOperationResult>("DeepDrftAudio.appendAudioBlock", playerId, audioBlock);
    }

    public async Task<AudioLoadResult> FinalizeAudioBufferAsync(string playerId)
    {
        return await InvokeJsAsync<AudioLoadResult>("DeepDrftAudio.finalizeAudioBuffer", playerId);
    }

    // Streaming methods
    public async Task<AudioOperationResult> InitializeStreaming(string playerId, long totalStreamLength)
    {
        return await InvokeJsAsync<AudioOperationResult>("DeepDrftAudio.initializeStreaming", playerId, totalStreamLength);
    }

    public async Task<StreamingResult> ProcessStreamingChunk(string playerId, byte[] audioChunk)
    {
        return await InvokeJsAsync<StreamingResult>("DeepDrftAudio.processStreamingChunk", playerId, audioChunk);
    }

    public async Task<AudioOperationResult> StartStreamingPlayback(string playerId)
    {
        return await InvokeJsAsync<AudioOperationResult>("DeepDrftAudio.startStreamingPlayback", playerId);
    }

    public async Task<AudioOperationResult> EnsureAudioContextReady(string playerId)
    {
        return await InvokeJsAsync<AudioOperationResult>("DeepDrftAudio.ensureAudioContextReady", playerId);
    }

    public async Task<AudioOperationResult> PlayAsync(string playerId)
    {
        return await InvokeJsAsync<AudioOperationResult>("DeepDrftAudio.play", playerId);
    }

    public async Task<AudioOperationResult> PauseAsync(string playerId)
    {
        return await InvokeJsAsync<AudioOperationResult>("DeepDrftAudio.pause", playerId);
    }

    public async Task<AudioOperationResult> StopAsync(string playerId)
    {
        return await InvokeJsAsync<AudioOperationResult>("DeepDrftAudio.stop", playerId);
    }

    public async Task<AudioOperationResult> UnloadAsync(string playerId)
    {
        return await InvokeJsAsync<AudioOperationResult>("DeepDrftAudio.unload", playerId);
    }

    public async Task<SeekResult> SeekAsync(string playerId, double position)
    {
        return await InvokeJsAsync<SeekResult>("DeepDrftAudio.seek", playerId, position);
    }

    // New methods for seek-beyond-buffer support
    public async Task<double> GetBufferedDuration(string playerId)
    {
        try
        {
            return await _jsRuntime.InvokeAsync<double>("DeepDrftAudio.getBufferedDuration", playerId);
        }
        catch
        {
            return 0;
        }
    }

    public async Task<long> CalculateByteOffset(string playerId, double positionSeconds)
    {
        try
        {
            return (long)await _jsRuntime.InvokeAsync<double>("DeepDrftAudio.calculateByteOffset", playerId, positionSeconds);
        }
        catch
        {
            return 0;
        }
    }

    public async Task<AudioOperationResult> ReinitializeFromOffset(string playerId, long totalStreamLength, double seekPosition)
    {
        return await InvokeJsAsync<AudioOperationResult>("DeepDrftAudio.reinitializeFromOffset", playerId, totalStreamLength, seekPosition);
    }

    public async Task<AudioOperationResult> SetVolumeAsync(string playerId, double volume)
    {
        return await InvokeJsAsync<AudioOperationResult>("DeepDrftAudio.setVolume", playerId, volume);
    }

    public async Task<double> GetCurrentTimeAsync(string playerId)
    {
        try
        {
            return await _jsRuntime.InvokeAsync<double>("DeepDrftAudio.getCurrentTime", playerId);
        }
        catch (Exception)
        {
            return 0;
        }
    }

    public async Task<AudioPlayerState?> GetStateAsync(string playerId)
    {
        try
        {
            return await _jsRuntime.InvokeAsync<AudioPlayerState>("DeepDrftAudio.getState", playerId);
        }
        catch (Exception)
        {
            return null;
        }
    }

    public async Task<AudioOperationResult> SetOnProgressCallbackAsync(string playerId, Func<double, Task> callback)
    {
        return await SetCallbackAsync(playerId, "_progress", "setOnProgressCallback", "OnProgressCallback", 
            wrapper => wrapper.OnProgress = callback);
    }

    public async Task<AudioOperationResult> SetOnEndCallbackAsync(string playerId, Func<Task> callback)
    {
        return await SetCallbackAsync(playerId, "_end", "setOnEndCallback", "OnEndCallback", 
            wrapper => wrapper.OnEnd = callback);
    }


    public async Task<AudioOperationResult> DisposePlayerAsync(string playerId)
    {
        CleanupPlayerCallbacks(playerId);
        return await InvokeJsAsync<AudioOperationResult>("DeepDrftAudio.disposePlayer", playerId);
    }

    private async Task<T> InvokeJsAsync<T>(string identifier, params object[] args)
    {
        try
        {
            return await _jsRuntime.InvokeAsync<T>(identifier, args);
        }
        catch (Exception ex)
        {
            if (typeof(T) == typeof(AudioOperationResult))
                return (T)(object)new AudioOperationResult { Success = false, Error = ex.Message };
            if (typeof(T) == typeof(AudioLoadResult))
                return (T)(object)new AudioLoadResult { Success = false, Error = ex.Message };
            if (typeof(T) == typeof(StreamingResult))
                return (T)(object)new StreamingResult { Success = false, Error = ex.Message };
            if (typeof(T) == typeof(SeekResult))
                return (T)(object)new SeekResult { Success = false, Error = ex.Message };
            throw;
        }
    }

    private async Task<AudioOperationResult> SetCallbackAsync(string playerId, string suffix, string jsMethod, string callbackMethod, Action<AudioPlayerCallback> configureCallback)
    {
        try
        {
            var callbackWrapper = new AudioPlayerCallback();
            configureCallback(callbackWrapper);
            
            var dotNetObjectRef = DotNetObjectReference.Create(callbackWrapper);
            _callbacks[playerId + suffix] = dotNetObjectRef;

            return await _jsRuntime.InvokeAsync<AudioOperationResult>($"DeepDrftAudio.{jsMethod}", 
                playerId, dotNetObjectRef, callbackMethod);
        }
        catch (Exception ex)
        {
            return new AudioOperationResult { Success = false, Error = ex.Message };
        }
    }

    private void CleanupPlayerCallbacks(string playerId)
    {
        var keysToRemove = _callbacks.Keys.Where(k => k.StartsWith(playerId + "_")).ToList();
        foreach (var key in keysToRemove)
        {
            _callbacks[key]?.Dispose();
            _callbacks.Remove(key);
        }
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var callback in _callbacks.Values)
        {
            callback?.Dispose();
        }
        _callbacks.Clear();
    }
}

public class AudioPlayerCallback
{
    public Func<double, Task>? OnProgress { get; set; }
    public Func<Task>? OnEnd { get; set; }

    [JSInvokable]
    public async Task OnProgressCallback(double currentTime)
    {
        if (OnProgress != null)
            await OnProgress(currentTime);
    }

    [JSInvokable]
    public async Task OnEndCallback()
    {
        if (OnEnd != null)
            await OnEnd();
    }
}

public class AudioOperationResult
{
    public bool Success { get; set; }
    public string? Error { get; set; }
}

public class SeekResult : AudioOperationResult
{
    public bool SeekBeyondBuffer { get; set; }
    public long ByteOffset { get; set; }
}

public class AudioLoadResult : AudioOperationResult
{
    public double Duration { get; set; }
    public int SampleRate { get; set; }
    public int NumberOfChannels { get; set; }
    public double LoadProgress { get; set; }
}

public class StreamingResult : AudioOperationResult
{
    public bool CanStartStreaming { get; set; }
    public bool HeaderParsed { get; set; }
    public int BufferCount { get; set; }
    public double? Duration { get; set; } // Duration in seconds calculated from WAV header
}

public class AudioPlayerState
{
    public bool IsPlaying { get; set; }
    public bool IsPaused { get; set; }
    public double CurrentTime { get; set; }
    public double Duration { get; set; }
    public double Volume { get; set; }
    public double LoadProgress { get; set; }
}