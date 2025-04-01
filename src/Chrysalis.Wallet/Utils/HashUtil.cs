namespace Chrysalis.Wallet.Utils;

public static class HashUtil
{
    /// <summary>
    /// Computes a Blake2b hash with the specified digest size.
    /// </summary>
    /// <param name="data">The data to hash.</param>
    /// <param name="digestSize">The size of the desired hash output (in bytes).</param>
    /// <returns>The Blake2b hash of the input data.</returns>
    private static byte[] Blake2b(byte[] data, int digestSize)
    {
        if (data == null)
            throw new ArgumentNullException(nameof(data), "Data cannot be null.");

        if (digestSize <= 0)
            throw new ArgumentOutOfRangeException(nameof(digestSize), "Digest size must be greater than 0.");

        return Blake2Fast.Blake2b.ComputeHash(digestSize, data);
    }

    /// <summary>
    /// Computes a Blake2b-224 hash (224-bit / 28-byte output).
    /// </summary>
    public static byte[] Blake2b224(byte[] data) => Blake2b(data, 28);

    /// <summary>
    /// Computes a Blake2b-256 hash (256-bit / 32-byte output).
    /// </summary>
    public static byte[] Blake2b256(byte[] data) => Blake2b(data, 32);

    /// <summary>
    /// Computes a Blake2b-160 hash (160-bit / 20-byte output).
    /// </summary>
    public static byte[] Blake2b160(byte[] data) => Blake2b(data, 20);
}