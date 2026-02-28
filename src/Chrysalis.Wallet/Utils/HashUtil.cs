
using Blake2Fast;

namespace Chrysalis.Wallet.Utils;

/// <summary>
/// Utility class for cryptographic hash operations using Blake2b.
/// </summary>
public static class HashUtil
{
    /// <summary>
    /// Computes a Blake2b-256 hash (256-bit / 32-byte output).
    /// </summary>
    /// <param name="data">The data to hash.</param>
    /// <returns>The 32-byte hash result.</returns>
    public static byte[] Blake2b256(byte[] data)
    {
        ArgumentNullException.ThrowIfNull(data);

        return Blake2b.HashData(32, data);
    }

    /// <summary>
    /// Computes a Blake2b-224 hash (224-bit / 28-byte output).
    /// </summary>
    /// <param name="data">The data to hash.</param>
    /// <returns>The 28-byte hash result.</returns>
    public static byte[] Blake2b224(byte[] data)
    {
        ArgumentNullException.ThrowIfNull(data);

        return Blake2b.HashData(28, data);
    }

    /// <summary>
    /// Computes a Blake2b-160 hash (160-bit / 20-byte output).
    /// </summary>
    /// <param name="data">The data to hash.</param>
    /// <returns>The 20-byte hash result.</returns>
    public static byte[] Blake2b160(byte[] data)
    {
        ArgumentNullException.ThrowIfNull(data);

        return Blake2b.HashData(20, data);
    }
}
