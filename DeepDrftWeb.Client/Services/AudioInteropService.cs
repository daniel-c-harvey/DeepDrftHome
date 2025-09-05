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

    public async Task<AudioLoadResult> LoadAudioFromUrlAsync(string playerId, string url)
    {
        try
        {
            var result = await _jsRuntime.InvokeAsync<AudioLoadResult>("DeepDrftAudio.loadAudioFromUrl", playerId, url);
            return result;
        }
        catch (Exception ex)
        {
            return new AudioLoadResult { Success = false, Error = ex.Message };
        }
    }


    public async Task<AudioOperationResult> PlayAsync(string playerId)
    {
        try
        {
            var result = await _jsRuntime.InvokeAsync<AudioOperationResult>("DeepDrftAudio.play", playerId);
            return result;
        }
        catch (Exception ex)
        {
            return new AudioOperationResult { Success = false, Error = ex.Message };
        }
    }

    public async Task<AudioOperationResult> PauseAsync(string playerId)
    {
        try
        {
            var result = await _jsRuntime.InvokeAsync<AudioOperationResult>("DeepDrftAudio.pause", playerId);
            return result;
        }
        catch (Exception ex)
        {
            return new AudioOperationResult { Success = false, Error = ex.Message };
        }
    }

    public async Task<AudioOperationResult> StopAsync(string playerId)
    {
        try
        {
            var result = await _jsRuntime.InvokeAsync<AudioOperationResult>("DeepDrftAudio.stop", playerId);
            return result;
        }
        catch (Exception ex)
        {
            return new AudioOperationResult { Success = false, Error = ex.Message };
        }
    }

    public async Task<AudioOperationResult> SeekAsync(string playerId, double position)
    {
        try
        {
            var result = await _jsRuntime.InvokeAsync<AudioOperationResult>("DeepDrftAudio.seek", playerId, position);
            return result;
        }
        catch (Exception ex)
        {
            return new AudioOperationResult { Success = false, Error = ex.Message };
        }
    }

    public async Task<AudioOperationResult> SetVolumeAsync(string playerId, double volume)
    {
        try
        {
            var result = await _jsRuntime.InvokeAsync<AudioOperationResult>("DeepDrftAudio.setVolume", playerId, volume);
            return result;
        }
        catch (Exception ex)
        {
            return new AudioOperationResult { Success = false, Error = ex.Message };
        }
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
        try
        {
            var callbackWrapper = new AudioPlayerCallback();
            callbackWrapper.OnProgress = callback;
            
            var dotNetObjectRef = DotNetObjectReference.Create(callbackWrapper);
            _callbacks[playerId + "_progress"] = dotNetObjectRef;

            var result = await _jsRuntime.InvokeAsync<AudioOperationResult>("DeepDrftAudio.setOnProgressCallback", 
                playerId, dotNetObjectRef, "OnProgressCallback");
            
            return result;
        }
        catch (Exception ex)
        {
            return new AudioOperationResult { Success = false, Error = ex.Message };
        }
    }

    public async Task<AudioOperationResult> SetOnEndCallbackAsync(string playerId, Func<Task> callback)
    {
        try
        {
            var callbackWrapper = new AudioPlayerCallback();
            callbackWrapper.OnEnd = callback;
            
            var dotNetObjectRef = DotNetObjectReference.Create(callbackWrapper);
            _callbacks[playerId + "_end"] = dotNetObjectRef;

            var result = await _jsRuntime.InvokeAsync<AudioOperationResult>("DeepDrftAudio.setOnEndCallback", 
                playerId, dotNetObjectRef, "OnEndCallback");
            
            return result;
        }
        catch (Exception ex)
        {
            return new AudioOperationResult { Success = false, Error = ex.Message };
        }
    }

    public async Task<AudioOperationResult> SetOnLoadProgressCallbackAsync(string playerId, Func<double, Task> callback)
    {
        try
        {
            var callbackWrapper = new AudioPlayerCallback();
            callbackWrapper.OnLoadProgress = callback;
            
            var dotNetObjectRef = DotNetObjectReference.Create(callbackWrapper);
            _callbacks[playerId + "_loadprogress"] = dotNetObjectRef;

            var result = await _jsRuntime.InvokeAsync<AudioOperationResult>("DeepDrftAudio.setOnLoadProgressCallback", 
                playerId, dotNetObjectRef, "OnLoadProgressCallback");
            
            return result;
        }
        catch (Exception ex)
        {
            return new AudioOperationResult { Success = false, Error = ex.Message };
        }
    }

    public async Task<AudioOperationResult> DisposePlayerAsync(string playerId)
    {
        try
        {
            // Clean up callbacks
            var keysToRemove = _callbacks.Keys.Where(k => k.StartsWith(playerId + "_")).ToList();
            foreach (var key in keysToRemove)
            {
                _callbacks[key]?.Dispose();
                _callbacks.Remove(key);
            }

            var result = await _jsRuntime.InvokeAsync<AudioOperationResult>("DeepDrftAudio.disposePlayer", playerId);
            return result;
        }
        catch (Exception ex)
        {
            return new AudioOperationResult { Success = false, Error = ex.Message };
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