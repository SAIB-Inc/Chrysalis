using System.Collections.Concurrent;

namespace Chrysalis.Tx.Utils;

public static class HexStringCache
{
    private static readonly ConcurrentDictionary<byte[], string> _bytesToHex =
        new(ByteArrayEqualityComparer.Instance);

    private static readonly ConcurrentDictionary<string, byte[]> _hexToBytes =
        new(StringComparer.OrdinalIgnoreCase);

    private const int MaxCacheSize = 50000;
    public static string ToHexString(byte[] bytes)
    {
        if (bytes == null || bytes.Length == 0)
            return string.Empty;

        if (_bytesToHex.TryGetValue(bytes, out string? cached))
            return cached;

        string result = Convert.ToHexString(bytes);

        if (_bytesToHex.Count < MaxCacheSize)
        {
            _bytesToHex.TryAdd(bytes, result);
        }

        return result;
    }

    public static byte[] FromHexString(string hex)
    {
        if (string.IsNullOrEmpty(hex))
            return [];

        hex = hex.ToUpperInvariant();

        if (_hexToBytes.TryGetValue(hex, out byte[]? cached))
            return cached;

        byte[] result = Convert.FromHexString(hex);

        if (_hexToBytes.Count < MaxCacheSize)
        {
            _hexToBytes.TryAdd(hex, result);
        }

        return result;
    }

    public static void ClearCache()
    {
        _bytesToHex.Clear();
        _hexToBytes.Clear();
    }

    public static (int BytesToHexCount, int HexToBytesCount) GetCacheStats()
    {
        return (_bytesToHex.Count, _hexToBytes.Count);
    }

}