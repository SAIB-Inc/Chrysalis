// File: src/Chrysalis.Tx/Utils/HexStringCache.cs

using System.Collections.Concurrent;

namespace Chrysalis.Tx.Utils;

/// <summary>
/// High-performance caching for hex string conversions to avoid repeated Convert.ToHexString/FromHexString calls
/// </summary>
public static class HexStringCache
{
    // Cache for byte[] -> string conversions (most common)
    private static readonly ConcurrentDictionary<byte[], string> _bytesToHex = 
        new(ByteArrayEqualityComparer.Instance);
    
    // Cache for string -> byte[] conversions
    private static readonly ConcurrentDictionary<string, byte[]> _hexToBytes = 
        new(StringComparer.OrdinalIgnoreCase);

    // Cache size limits to prevent memory leaks
    private const int MaxCacheSize = 50000;
    
    /// <summary>
    /// Converts byte array to hex string with caching
    /// </summary>
    public static string ToHexString(byte[] bytes)
    {
        if (bytes == null || bytes.Length == 0) 
            return string.Empty;

        // Try to get from cache first
        if (_bytesToHex.TryGetValue(bytes, out string? cached))
            return cached;

        // Calculate new value
        string result = Convert.ToHexString(bytes);
        
        // Cache the result (with size limit)
        if (_bytesToHex.Count < MaxCacheSize)
        {
            _bytesToHex.TryAdd(bytes, result);
        }
        
        return result;
    }

    /// <summary>
    /// Converts hex string to byte array with caching
    /// </summary>
    public static byte[] FromHexString(string hex)
    {
        if (string.IsNullOrEmpty(hex)) 
            return [];

        // Normalize to uppercase for consistent caching
        hex = hex.ToUpperInvariant();
        
        // Try to get from cache first
        if (_hexToBytes.TryGetValue(hex, out byte[]? cached))
            return cached;

        // Calculate new value
        byte[] result = Convert.FromHexString(hex);
        
        // Cache the result (with size limit)
        if (_hexToBytes.Count < MaxCacheSize)
        {
            _hexToBytes.TryAdd(hex, result);
        }
        
        return result;
    }

    /// <summary>
    /// Clears both caches (useful for memory management in long-running applications)
    /// </summary>
    public static void ClearCache()
    {
        _bytesToHex.Clear();
        _hexToBytes.Clear();
    }

    /// <summary>
    /// Gets current cache statistics for monitoring
    /// </summary>
    public static (int BytesToHexCount, int HexToBytesCount) GetCacheStats()
    {
        return (_bytesToHex.Count, _hexToBytes.Count);
    }

    /// <summary>
    /// Pre-warms the cache with common values (optional optimization)
    /// </summary>
    public static void PreWarmCache()
    {
        // Pre-cache common policy ID and asset name patterns
        var commonSizes = new[] { 28, 32, 0, 1, 2, 4, 8, 16 }; // Common byte array sizes in Cardano
        var random = new Random(42); // Fixed seed for consistent pre-warming
        
        foreach (var size in commonSizes)
        {
            for (int i = 0; i < 100; i++) // Pre-cache 100 examples of each size
            {
                var bytes = new byte[size];
                if (size > 0)
                {
                    random.NextBytes(bytes);
                    ToHexString(bytes); // This will cache it
                }
            }
        }
    }
}