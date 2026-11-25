
using Blake2Fast;

namespace Chrysalis.Wallet.Utils;

/// <summary>
/// Utility class for cryptographic hash operations using Blake2b.
/// </summary>
public static class HashUtil
{
    internal const int Blake2b224Size = 28;

    /// <summary>
    /// Computes a Blake2b-256 hash (256-bit / 32-byte output).
    /// </summary>
    /// <param name="data">The data to hash.</param>
    /// <returns>The 32-byte hash result.</returns>
    public static byte[] Blake2b256(byte[] data)
    {
        ArgumentNullException.ThrowIfNull(data);

        return Blake2b256(data.AsSpan());
    }

    /// <summary>
    /// Computes a Blake2b-256 hash (256-bit / 32-byte output).
    /// </summary>
    /// <param name="data">The data to hash.</param>
    /// <returns>The 32-byte hash result.</returns>
    public static byte[] Blake2b256(ReadOnlySpan<byte> data) => Blake2b.HashData(32, data);

    /// <summary>
    /// Computes a Blake2b-224 hash (224-bit / 28-byte output).
    /// </summary>
    /// <param name="data">The data to hash.</param>
    /// <returns>The 28-byte hash result.</returns>
    public static byte[] Blake2b224(byte[] data)
    {
        ArgumentNullException.ThrowIfNull(data);

        return Blake2b224(data.AsSpan());
    }

    /// <summary>
    /// Computes a Blake2b-224 hash (224-bit / 28-byte output).
    /// </summary>
    /// <param name="data">The data to hash.</param>
    /// <returns>The 28-byte hash result.</returns>
    public static byte[] Blake2b224(ReadOnlySpan<byte> data) => Blake2b.HashData(Blake2b224Size, data);

    /// <summary>
    /// Computes a Blake2b-160 hash (160-bit / 20-byte output).
    /// </summary>
    /// <param name="data">The data to hash.</param>
    /// <returns>The 20-byte hash result.</returns>
    public static byte[] Blake2b160(byte[] data)
    {
        ArgumentNullException.ThrowIfNull(data);

        return Blake2b160(data.AsSpan());
    }

    /// <summary>
    /// Computes a Blake2b-160 hash (160-bit / 20-byte output).
    /// </summary>
    /// <param name="data">The data to hash.</param>
    /// <returns>The 20-byte hash result.</returns>
    public static byte[] Blake2b160(ReadOnlySpan<byte> data) => Blake2b.HashData(20, data);
}
