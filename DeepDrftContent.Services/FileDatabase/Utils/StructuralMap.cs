using System.Collections;
using System.Text.Json;

namespace DeepDrftContent.Services.FileDatabase.Utils;

/// <summary>
/// A map implementation that uses structural equality for keys by serializing them to JSON.
/// This provides the same behavior as the TypeScript StructuralMap.
/// Optimized with caching to avoid repeated serialization.
/// </summary>
/// <typeparam name="TKey">The key type</typeparam>
/// <typeparam name="TValue">The value type</typeparam>
public class StructuralMap<TKey, TValue> : IEnumerable<KeyValuePair<TKey, TValue>> where TKey : notnull
{
    private readonly Dictionary<string, KeyValuePair<TKey, TValue>> _innerMap = new();
    private readonly Dictionary<TKey, string> _keyStringCache = new();

    /// <summary>
    /// Converts a key to its string representation for structural comparison
    /// Uses caching to avoid expensive serialization on repeated lookups
    /// </summary>
    private string GetKeyString(TKey key)
    {
        if (key == null) return "null";
        
        // For reference types, use cache to avoid repeated serialization
        if (!typeof(TKey).IsValueType && _keyStringCache.TryGetValue(key, out var cached))
        {
            return cached;
        }

        var keyString = key switch
        {
            string s => s,
            int or long or float or double or decimal => key.ToString()!,
            _ => JsonSerializer.Serialize(key)
        };

        // Cache for reference types only (value types are cheap to convert)
        if (!typeof(TKey).IsValueType)
        {
            _keyStringCache[key] = keyString;
        }

        return keyString;
    }

    /// <summary>
    /// Sets a key-value pair in the map
    /// </summary>
    public StructuralMap<TKey, TValue> Set(TKey key, TValue value)
    {
        var keyString = GetKeyString(key);
        _innerMap[keyString] = new KeyValuePair<TKey, TValue>(key, value);
        return this;
    }

    /// <summary>
    /// Gets a value by key, or default if not found
    /// </summary>
    public TValue? Get(TKey key)
    {
        var keyString = GetKeyString(key);
        return _innerMap.TryGetValue(keyString, out var pair) ? pair.Value : default;
    }

    /// <summary>
    /// Checks if the map contains the specified key
    /// </summary>
    public bool Has(TKey key)
    {
        var keyString = GetKeyString(key);
        return _innerMap.ContainsKey(keyString);
    }

    /// <summary>
    /// Removes a key-value pair from the map
    /// </summary>
    public bool Delete(TKey key)
    {
        var keyString = GetKeyString(key);
        return _innerMap.Remove(keyString);
    }

    /// <summary>
    /// Clears all entries from the map
    /// </summary>
    public void Clear() => _innerMap.Clear();

    /// <summary>
    /// Gets the number of entries in the map
    /// </summary>
    public int Size => _innerMap.Count;

    /// <summary>
    /// Enumerates all key-value pairs
    /// </summary>
    public IEnumerator<KeyValuePair<TKey, TValue>> GetEnumerator()
    {
        return _innerMap.Values.GetEnumerator();
    }

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    /// <summary>
    /// Gets all keys in the map
    /// </summary>
    public IEnumerable<TKey> Keys => _innerMap.Values.Select(pair => pair.Key);

    /// <summary>
    /// Gets all values in the map
    /// </summary>
    public IEnumerable<TValue> Values => _innerMap.Values.Select(pair => pair.Value);

    /// <summary>
    /// Executes a callback for each key-value pair
    /// </summary>
    public void ForEach(Action<TValue, TKey, StructuralMap<TKey, TValue>> callback)
    {
        foreach (var (key, value) in this)
        {
            callback(value, key, this);
        }
    }
}
