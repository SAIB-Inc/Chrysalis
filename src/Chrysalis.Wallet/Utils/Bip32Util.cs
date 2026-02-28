using Chrysalis.Crypto;
using Chrysalis.Wallet.Models.Enums;

namespace Chrysalis.Wallet.Utils;

/// <summary>
/// Utility class for BIP-32 key derivation operations used in Cardano's hierarchical deterministic wallets.
/// </summary>
public static class Bip32Util
{
    /// <summary>
    /// Performs scalar addition with 28-byte truncation and multiplication by 8,
    /// which is part of Cardano's key derivation scheme.
    /// </summary>
    /// <param name="x">The first 32-byte input array.</param>
    /// <param name="y">The second 32-byte input array.</param>
    /// <returns>A 32-byte result array.</returns>
    public static byte[] Add28Mul8(byte[] x, byte[] y)
    {
        ArgumentNullException.ThrowIfNull(x);
        ArgumentNullException.ThrowIfNull(y);

        if (x.Length != 32)
        {
            throw new ArgumentException("x is incorrect length", nameof(x));
        }

        if (y.Length != 32)
        {
            throw new ArgumentException("y is incorrect length", nameof(y));
        }

        ushort carry = 0;
        byte[] res = new byte[32];

        for (int i = 0; i < 28; i++)
        {
            int r = x[i] + (y[i] << 3) + carry;
            res[i] = (byte)(r & 0xff);
            carry = (ushort)(r >> 8);
        }

        for (int j = 28; j < 32; j++)
        {
            int r = x[j] + carry;
            res[j] = (byte)(r & 0xff);
            carry = (ushort)(r >> 8);
        }

        return res;
    }

    /// <summary>
    /// Handles 256-bit addition with carry, needed for the right half of key derivation.
    /// </summary>
    /// <param name="x">The first 32-byte input array.</param>
    /// <param name="y">The second 32-byte input array.</param>
    /// <returns>A 32-byte result array.</returns>
    public static byte[] Add256Bits(byte[] x, byte[] y)
    {
        ArgumentNullException.ThrowIfNull(x);
        ArgumentNullException.ThrowIfNull(y);

        if (x.Length != 32)
        {
            throw new ArgumentException("x is incorrect length", nameof(x));
        }

        if (y.Length != 32)
        {
            throw new ArgumentException("y is incorrect length", nameof(y));
        }

        ushort carry = 0;
        byte[] res = new byte[32];

        for (int i = 0; i < 32; i++)
        {
            int r = x[i] + y[i] + carry;
            res[i] = (byte)r;
            carry = (ushort)(r >> 8);
        }

        return res;
    }

    /// <summary>
    /// Computes the Ed25519 public key point from a truncated scalar multiplied by 8.
    /// </summary>
    /// <param name="sk">The secret key bytes.</param>
    /// <returns>The computed public key bytes.</returns>
    public static byte[] PointOfTrunc28Mul8(byte[] sk)
    {
        ArgumentNullException.ThrowIfNull(sk);

        byte[] kl = new byte[32];
        byte[] copy = Add28Mul8(kl, sk);
        return Ed25519.GetPublicKey(copy);
    }

    /// <summary>
    /// Converts a 32-bit integer to little-endian byte array format.
    /// </summary>
    /// <param name="i">The integer value to convert.</param>
    /// <returns>A 4-byte little-endian representation.</returns>
    public static byte[] Le32(ulong i)
    {
        return [(byte)i, (byte)(i >> 8), (byte)(i >> 16), (byte)(i >> 24)];
    }

    /// <summary>
    /// Validates whether a BIP-32 derivation path string has the correct format.
    /// </summary>
    /// <param name="path">The derivation path string (e.g., "m/1852'/1815'/0'").</param>
    /// <returns>True if the path is valid; otherwise false.</returns>
    public static bool IsValidPath(string path)
    {
        ArgumentNullException.ThrowIfNull(path);

        return !path.Split('/').Skip(1).Select(a => a.Replace("'", "", StringComparison.Ordinal)).Any(a => !int.TryParse(a, out _));
    }

    /// <summary>
    /// Determines the derivation type (hard or soft) from a derivation index.
    /// </summary>
    /// <param name="index">The derivation index.</param>
    /// <returns>The derivation type based on whether the index is hardened.</returns>
    public static DerivationType FromIndex(ulong index)
    {
        return index >= 0x80000000 ? DerivationType.HARD : DerivationType.SOFT;
    }
}
