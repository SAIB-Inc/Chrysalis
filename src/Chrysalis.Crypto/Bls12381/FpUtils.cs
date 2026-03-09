using System.Numerics;

namespace Chrysalis.Crypto.Bls12381;

/// <summary>
/// Shared utilities for field arithmetic.
/// </summary>
internal static class FpUtils
{
    /// <summary>
    /// Always-positive modular reduction.
    /// C# BigInteger % can return negative for negative dividends.
    /// </summary>
    internal static BigInteger PosMod(BigInteger a, BigInteger b)
    {
        BigInteger r = a % b;
        return r >= 0 ? r : r + b;
    }

    /// <summary>
    /// Convert BigInteger to big-endian byte array of specified length.
    /// </summary>
    internal static byte[] NumberToBytesBE(BigInteger n, int length)
    {
        byte[] bytes = n.ToByteArray(isUnsigned: true, isBigEndian: true);
        if (bytes.Length == length)
        {
            return bytes;
        }

        if (bytes.Length > length)
        {
            return bytes[^length..];
        }

        byte[] padded = new byte[length];
        bytes.CopyTo(padded.AsSpan(length - bytes.Length));
        return padded;
    }

    /// <summary>
    /// Convert big-endian bytes to BigInteger (unsigned).
    /// </summary>
    internal static BigInteger BytesToNumberBE(ReadOnlySpan<byte> bytes) =>
        new(bytes, isUnsigned: true, isBigEndian: true);

    /// <summary>
    /// Bit length of a BigInteger.
    /// </summary>
    internal static int BitLen(BigInteger n) => n <= 0 ? 0 : (int)n.GetBitLength();

    /// <summary>
    /// Get bit at position i (0 = LSB).
    /// </summary>
    internal static bool BitGet(BigInteger n, int i) =>
        ((n >> i) & 1) == 1;

    /// <summary>
    /// Montgomery batch inversion.
    /// Inverts N elements using a single field inversion + 3(N-1) multiplications.
    /// </summary>
    internal static BigInteger[] InvertBatch(
        BigInteger[] nums,
        BigInteger modulus,
        Func<BigInteger, BigInteger> inv)
    {
        BigInteger[] result = new BigInteger[nums.Length];
        BigInteger[] scratch = new BigInteger[nums.Length];
        BigInteger acc = 1;

        for (int i = 0; i < nums.Length; i++)
        {
            if (PosMod(nums[i], modulus) == 0)
            {
                scratch[i] = 0;
                continue;
            }
            scratch[i] = acc;
            acc = PosMod(acc * nums[i], modulus);
        }

        acc = inv(acc);

        for (int i = nums.Length - 1; i >= 0; i--)
        {
            if (PosMod(nums[i], modulus) == 0)
            {
                result[i] = 0;
                continue;
            }
            result[i] = PosMod(acc * scratch[i], modulus);
            acc = PosMod(acc * nums[i], modulus);
        }

        return result;
    }
}
