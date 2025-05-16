
using Blake2Fast;

namespace Chrysalis.Wallet.Utils;

public static class HashUtil
{
    /// <summary>
    /// Computes a Blake2b-256 hash with the specified digest size.
    /// </summary>
    /// <param name="data">The data to hash.</param>
    public static byte[] Blake2b256(byte[] data) => Blake2b.HashData(32, data);

    /// <summary>
    /// Computes a Blake2b-224 hash (224-bit / 28-byte output).
    /// </summary>
    public static byte[] Blake2b224(byte[] data) => Blake2b.HashData(28, data);

    /// <summary>
    /// Computes a Blake2b-160 hash (160-bit / 20-byte output).
    /// </summary>
    public static byte[] Blake2b160(byte[] data) => Blake2b.HashData(20, data);
}