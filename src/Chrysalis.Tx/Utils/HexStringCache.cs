using System.Collections.Concurrent;

namespace Chrysalis.Tx.Utils;

/// <summary>
/// Thread-safe cache for hex string conversions to reduce repeated allocations.
/// </summary>
public static class HexStringCache
{
    private static readonly ConcurrentDictionary<byte[], string> BytesToHex =
        new(ByteArrayEqualityComparer.Instance);

    private static readonly ConcurrentDictionary<string, byte[]> HexToBytes =
        new(StringComparer.OrdinalIgnoreCase);

    private const int MaxCacheSize = 50000;

    /// <summary>
    /// Converts a byte array to its hex string representation, using a cache for repeated lookups.
    /// </summary>
    /// <param name="bytes">The byte array to convert.</param>
    /// <returns>The hex string representation.</returns>
    public static string ToHexString(byte[] bytes)
    {
        if (bytes == null || bytes.Length == 0)
        {
            return string.Empty;
        }

        if (BytesToHex.TryGetValue(bytes, out string? cached))
        {
            return cached;
        }

        string result = Convert.ToHexString(bytes);

        if (BytesToHex.Count < MaxCacheSize)
        {
            _ = BytesToHex.TryAdd(bytes, result);
        }

        return result;
    }

    /// <summary>
    /// Converts a hex string to a byte array, using a cache for repeated lookups.
    /// </summary>
    /// <param name="hex">The hex string to convert.</param>
    /// <returns>The byte array representation.</returns>
    public static byte[] FromHexString(string hex)
    {
        if (string.IsNullOrEmpty(hex))
        {
            return [];
        }

        hex = hex.ToUpperInvariant();

        if (HexToBytes.TryGetValue(hex, out byte[]? cached))
        {
            return cached;
        }

        byte[] result = Convert.FromHexString(hex);

        if (HexToBytes.Count < MaxCacheSize)
        {
            _ = HexToBytes.TryAdd(hex, result);
        }

        return result;
    }

    /// <summary>
    /// Clears both hex conversion caches.
    /// </summary>
    public static void ClearCache()
    {
        BytesToHex.Clear();
        HexToBytes.Clear();
    }

    /// <summary>
    /// Gets the current cache sizes for monitoring.
    /// </summary>
    public static (int BytesToHexCount, int HexToBytesCount) CacheStats => (BytesToHex.Count, HexToBytes.Count);
}
