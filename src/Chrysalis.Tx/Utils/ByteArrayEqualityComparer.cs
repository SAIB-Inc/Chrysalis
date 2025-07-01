using System.Collections.Concurrent;

namespace Chrysalis.Tx.Utils;


public sealed class ByteArrayEqualityComparer : IEqualityComparer<byte[]>
{

    public static readonly ByteArrayEqualityComparer Instance = new();

    private readonly ConcurrentDictionary<byte[], int> _hashCodeCache = new();

    private ByteArrayEqualityComparer() { }

    public bool Equals(byte[]? x, byte[]? y)
    {
        if (ReferenceEquals(x, y)) return true;

        if (x is null || y is null) return false;

        if (x.Length != y.Length) return false;

        return x.AsSpan().SequenceEqual(y.AsSpan());
    }


    public int GetHashCode(byte[] obj)
    {
        if (obj is null) return 0;

        if (_hashCodeCache.TryGetValue(obj, out int cachedHash))
            return cachedHash;

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
            _hashCodeCache.TryAdd(obj, result);
        }

        return result;
    }

    public void ClearCache()
    {
        _hashCodeCache.Clear();
    }

    public int CacheSize => _hashCodeCache.Count;
}