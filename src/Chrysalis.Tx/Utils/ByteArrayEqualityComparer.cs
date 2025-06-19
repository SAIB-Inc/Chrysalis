// File: src/Chrysalis.Tx/Utils/ByteArrayEqualityComparer.cs

using System.Collections.Concurrent;

namespace Chrysalis.Tx.Utils;

/// <summary>
/// High-performance equality comparer for byte arrays using spans and hash code caching
/// </summary>
public sealed class ByteArrayEqualityComparer : IEqualityComparer<byte[]>
{
    /// <summary>
    /// Singleton instance to avoid creating multiple comparers
    /// </summary>
    public static readonly ByteArrayEqualityComparer Instance = new();

    // Cache hash codes to avoid recalculating for the same byte arrays
    private readonly ConcurrentDictionary<byte[], int> _hashCodeCache = new();

    private ByteArrayEqualityComparer() { }

    /// <summary>
    /// Compares two byte arrays for equality using high-performance span comparison
    /// </summary>
    public bool Equals(byte[]? x, byte[]? y)
    {
        // Fast path: reference equality
        if (ReferenceEquals(x, y)) return true;
        
        // Fast path: null checks
        if (x is null || y is null) return false;
        
        // Fast path: length check
        if (x.Length != y.Length) return false;
        
        // Use span-based comparison for better performance
        return x.AsSpan().SequenceEqual(y.AsSpan());
    }

    /// <summary>
    /// Generates hash code for byte array with caching for better performance
    /// </summary>
    public int GetHashCode(byte[] obj)
    {
        if (obj is null) return 0;
        
        // Try to get cached hash code first
        if (_hashCodeCache.TryGetValue(obj, out int cachedHash))
            return cachedHash;

        // Calculate hash code using HashCode struct for better distribution
        var hash = new HashCode();
        
        // For performance, only hash first few bytes if array is large
        var span = obj.AsSpan();
        var length = Math.Min(span.Length, 32); // Hash first 32 bytes max
        
        for (int i = 0; i < length; i++)
        {
            hash.Add(span[i]);
        }
        
        // Also include the length to differentiate arrays of different sizes
        hash.Add(obj.Length);
        
        int result = hash.ToHashCode();
        
        // Cache the result (but don't let cache grow indefinitely)
        if (_hashCodeCache.Count < 10000) // Reasonable cache limit
        {
            _hashCodeCache.TryAdd(obj, result);
        }
        
        return result;
    }

    /// <summary>
    /// Clears the hash code cache (useful for memory management in long-running applications)
    /// </summary>
    public void ClearCache()
    {
        _hashCodeCache.Clear();
    }

    /// <summary>
    /// Gets the current cache size (useful for monitoring)
    /// </summary>
    public int CacheSize => _hashCodeCache.Count;
}