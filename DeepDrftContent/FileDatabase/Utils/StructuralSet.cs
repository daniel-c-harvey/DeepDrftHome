using System.Collections;
using System.Text.Json;

namespace DeepDrftContent.FileDatabase.Utils;

/// <summary>
/// A set implementation that uses structural equality for values by serializing them to JSON.
/// This provides the same behavior as the TypeScript StructuralSet.
/// </summary>
/// <typeparam name="T">The value type</typeparam>
public class StructuralSet<T> : IEnumerable<T>
{
    private readonly Dictionary<string, T> _innerMap = new();

    /// <summary>
    /// Converts a value to its string representation for structural comparison
    /// </summary>
    private string GetValueString(T value)
    {
        return value switch
        {
            null => "null",
            string s => s,
            int or long or float or double or decimal => value.ToString()!,
            _ => JsonSerializer.Serialize(value)
        };
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
