using System.Collections.Concurrent;

namespace Chrysalis.Tx.Utils;

/// <summary>
/// Provides equality comparison for byte arrays using sequence equality.
/// </summary>
public sealed class ByteArrayEqualityComparer : IEqualityComparer<byte[]>
{
    /// <summary>
    /// Gets the singleton instance of the comparer.
    /// </summary>
    public static readonly ByteArrayEqualityComparer Instance = new();

    private readonly ConcurrentDictionary<byte[], int> _hashCodeCache = new();

    private ByteArrayEqualityComparer() { }

    /// <summary>
    /// Determines whether two byte arrays are equal by comparing their sequences.
    /// </summary>
    /// <param name="x">The first byte array.</param>
    /// <param name="y">The second byte array.</param>
    /// <returns>True if the arrays are sequence-equal; otherwise, false.</returns>
    public bool Equals(byte[]? x, byte[]? y)
    {
        return ReferenceEquals(x, y) || (x is not null && y is not null && x.Length == y.Length && x.AsSpan().SequenceEqual(y.AsSpan()));
    }

    /// <summary>
    /// Returns a hash code for the specified byte array.
    /// </summary>
    /// <param name="obj">The byte array to hash.</param>
    /// <returns>A hash code for the byte array.</returns>
    public int GetHashCode(byte[] obj)
    {
        ArgumentNullException.ThrowIfNull(obj);

        if (_hashCodeCache.TryGetValue(obj, out int cachedHash))
        {
            return cachedHash;
        }

        HashCode hash = new();

        Span<byte> span = obj.AsSpan();
        int length = Math.Min(span.Length, 32);

        for (int i = 0; i < length; i++)
        {
            hash.Add(span[i]);
        }

        hash.Add(obj.Length);

        int result = hash.ToHashCode();

        if (_hashCodeCache.Count < 10000)
        {
            _ = _hashCodeCache.TryAdd(obj, result);
        }

        return result;
    }

    /// <summary>
    /// Clears the internal hash code cache.
    /// </summary>
    public void ClearCache()
    {
        _hashCodeCache.Clear();
    }

    /// <summary>
    /// Gets the current number of entries in the hash code cache.
    /// </summary>
    public int CacheSize => _hashCodeCache.Count;
}
