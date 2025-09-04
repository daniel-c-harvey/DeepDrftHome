using System.Collections;
using System.Text.Json;

namespace DeepDrftContent.FileDatabase.Utils;

/// <summary>
/// A set implementation that uses structural equality for values by serializing them to JSON.
/// This provides the same behavior as the TypeScript StructuralSet.
/// Optimized with caching to avoid repeated serialization.
/// </summary>
/// <typeparam name="T">The value type</typeparam>
public class StructuralSet<T> : IEnumerable<T> where T : notnull
{
    private readonly Dictionary<string, T> _innerMap = new();
    private readonly Dictionary<T, string> _valueStringCache = new();

    /// <summary>
    /// Converts a value to its string representation for structural comparison
    /// Uses caching to avoid expensive serialization on repeated lookups
    /// </summary>
    private string GetValueString(T value)
    {
        if (value == null) return "null";
        
        // For reference types, use cache to avoid repeated serialization
        if (!typeof(T).IsValueType && _valueStringCache.TryGetValue(value, out var cached))
        {
            return cached;
        }

        var valueString = value switch
        {
            string s => s,
            int or long or float or double or decimal => value.ToString()!,
            _ => JsonSerializer.Serialize(value)
        };

        // Cache for reference types only (value types are cheap to convert)
        if (!typeof(T).IsValueType)
        {
            _valueStringCache[value] = valueString;
        }

        return valueString;
    }

    /// <summary>
    /// Adds a value to the set
    /// </summary>
    public StructuralSet<T> Add(T value)
    {
        var valueString = GetValueString(value);
        if (!_innerMap.ContainsKey(valueString))
        {
            _innerMap[valueString] = value;
        }
        return this;
    }

    /// <summary>
    /// Checks if the set contains the specified value
    /// </summary>
    public bool Has(T value)
    {
        var valueString = GetValueString(value);
        return _innerMap.ContainsKey(valueString);
    }

    /// <summary>
    /// Removes a value from the set
    /// </summary>
    public bool Delete(T value)
    {
        var valueString = GetValueString(value);
        return _innerMap.Remove(valueString);
    }

    /// <summary>
    /// Clears all values from the set
    /// </summary>
    public void Clear() => _innerMap.Clear();

    /// <summary>
    /// Gets the number of values in the set
    /// </summary>
    public int Size => _innerMap.Count;

    /// <summary>
    /// Enumerates all values in the set
    /// </summary>
    public IEnumerator<T> GetEnumerator()
    {
        return _innerMap.Values.GetEnumerator();
    }

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    /// <summary>
    /// Gets all values in the set
    /// </summary>
    public IEnumerable<T> Values => _innerMap.Values;
}
