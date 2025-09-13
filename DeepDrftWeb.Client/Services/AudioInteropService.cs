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

    public async Task<AudioOperationResult> SeekAsync(string playerId, double position)
    {
        return await InvokeJsAsync<AudioOperationResult>("DeepDrftAudio.seek", playerId, position);
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

    public async Task<AudioOperationResult> SetOnLoadProgressCallbackAsync(string playerId, Func<double, Task> callback)
    {
        return await SetCallbackAsync(playerId, "_loadprogress", "setOnLoadProgressCallback", "OnLoadProgressCallback", 
            wrapper => wrapper.OnLoadProgress = callback);
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
    public Func<double, Task>? OnLoadProgress { get; set; }

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

    [JSInvokable]
    public async Task OnLoadProgressCallback(double progress)
    {
        if (OnLoadProgress != null)
            await OnLoadProgress(progress);
    }
}

public class AudioOperationResult
{
    public bool Success { get; set; }
    public string? Error { get; set; }
}

public class AudioLoadResult : AudioOperationResult
{
    public double Duration { get; set; }
    public int SampleRate { get; set; }
    public int NumberOfChannels { get; set; }
    public double LoadProgress { get; set; }
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