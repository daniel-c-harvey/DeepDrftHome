using DeepDrftContent.Services.FileDatabase.Models;
using Microsoft.Extensions.Logging;

namespace DeepDrftContent.Services.FileDatabase.Services;

/// <summary>
/// Watches index files for external modifications and triggers reloads.
/// Uses FileSystemWatcher to detect changes made by other processes (e.g., CLI).
/// </summary>
public class IndexWatcher : IDisposable
{
    private readonly Dictionary<string, FileSystemWatcher> _watchers = new();
    private readonly Dictionary<string, Action> _reloadCallbacks = new();
    private readonly object _lock = new();
    private readonly ILogger<IndexWatcher>? _logger;
    private bool _disposed;

    public IndexWatcher(ILogger<IndexWatcher>? logger = null)
    {
        _logger = logger;
    }

    /// <summary>
    /// Registers an index file to be watched for changes.
    /// </summary>
    /// <param name="indexPath">Full path to the directory containing the index file</param>
    /// <param name="onChanged">Callback to invoke when the index file changes</param>
    public void Watch(string indexPath, Action onChanged)
    {
        lock (_lock)
        {
            if (_disposed) return;

            // Already watching this path
            if (_watchers.ContainsKey(indexPath))
            {
                _reloadCallbacks[indexPath] = onChanged;
                return;
            }

            try
            {
                var watcher = new FileSystemWatcher(indexPath)
                {
                    Filter = "index",
                    NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.CreationTime,
                    EnableRaisingEvents = true
                };

                watcher.Changed += OnIndexChanged;
                watcher.Created += OnIndexChanged;

                _watchers[indexPath] = watcher;
                _reloadCallbacks[indexPath] = onChanged;

                if (_logger != null)
                    _logger.LogDebug("IndexWatcher: Watching {IndexPath}/index", indexPath);
                else
                    Console.WriteLine($"IndexWatcher: Watching {indexPath}/index");
            }
            catch (Exception ex)
            {
                if (_logger != null)
                    _logger.LogWarning(ex, "IndexWatcher: Failed to watch {IndexPath}", indexPath);
                else
                    Console.WriteLine($"IndexWatcher: Failed to watch {indexPath}: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// Stops watching an index file.
    /// </summary>
    public void Unwatch(string indexPath)
    {
        lock (_lock)
        {
            if (_watchers.TryGetValue(indexPath, out var watcher))
            {
                watcher.EnableRaisingEvents = false;
                watcher.Dispose();
                _watchers.Remove(indexPath);
                _reloadCallbacks.Remove(indexPath);
            }
        }
    }

    private void OnIndexChanged(object sender, FileSystemEventArgs e)
    {
        var watcher = sender as FileSystemWatcher;
        if (watcher == null) return;

        var indexPath = watcher.Path;

        lock (_lock)
        {
            if (_reloadCallbacks.TryGetValue(indexPath, out var callback))
            {
                if (_logger != null)
                    _logger.LogDebug("IndexWatcher: Index changed at {IndexPath}, triggering reload", indexPath);
                else
                    Console.WriteLine($"IndexWatcher: Index changed at {indexPath}, triggering reload");

                // Invoke callback on a background thread to avoid blocking the watcher
                Task.Run(() =>
                {
                    try
                    {
                        callback();
                    }
                    catch (Exception ex)
                    {
                        if (_logger != null)
                            _logger.LogWarning(ex, "IndexWatcher: Reload callback failed for {IndexPath}", indexPath);
                        else
                            Console.WriteLine($"IndexWatcher: Reload callback failed: {ex.Message}");
                    }
                });
            }
        }
    }

    public void Dispose()
    {
        lock (_lock)
        {
            if (_disposed) return;
            _disposed = true;

            foreach (var watcher in _watchers.Values)
            {
                watcher.EnableRaisingEvents = false;
                watcher.Dispose();
            }
            _watchers.Clear();
            _reloadCallbacks.Clear();
        }
    }
}
