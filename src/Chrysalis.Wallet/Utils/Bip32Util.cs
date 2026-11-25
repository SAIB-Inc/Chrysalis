// Contains code adapted from CardanoSharp.Wallet (MIT), Copyright (c) 2021 CardanoSharp; changed for Chrysalis. See THIRD-PARTY-NOTICES.md.
using System.Buffers.Binary;
using System.Globalization;
using System.Security.Cryptography;
using Chrysalis.Crypto;
using Chrysalis.Wallet.Models.Enums;

namespace Chrysalis.Wallet.Utils;

/// <summary>
/// Utility class for BIP-32 key derivation operations used in Cardano's hierarchical deterministic wallets.
/// </summary>
public static class Bip32Util
{
    internal const uint FirstHardenedIndex = 0x80000000;
    internal const int ChainCodeSize = 32;

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

        byte[] res = new byte[32];
        Add28Mul8(x, y, res);
        return res;
    }

    internal static void Add28Mul8(ReadOnlySpan<byte> x, ReadOnlySpan<byte> y, Span<byte> destination)
    {
        ushort carry = 0;

        for (int i = 0; i < 28; i++)
        {
            int r = x[i] + (y[i] << 3) + carry;
            destination[i] = (byte)(r & 0xff);
            carry = (ushort)(r >> 8);
        }

        for (int j = 28; j < 32; j++)
        {
            int r = x[j] + carry;
            destination[j] = (byte)(r & 0xff);
            carry = (ushort)(r >> 8);
        }
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

        byte[] res = new byte[32];
        Add256Bits(x, y, res);
        return res;
    }

    internal static void Add256Bits(ReadOnlySpan<byte> x, ReadOnlySpan<byte> y, Span<byte> destination)
    {
        ushort carry = 0;

        for (int i = 0; i < 32; i++)
        {
            int r = x[i] + y[i] + carry;
            destination[i] = (byte)r;
            carry = (ushort)(r >> 8);
        }
    }

    /// <summary>
    /// Computes the Ed25519 public key point from a truncated scalar multiplied by 8.
    /// </summary>
    /// <param name="sk">The 32-byte scalar bytes.</param>
    /// <returns>The computed public key bytes.</returns>
    public static byte[] PointOfTrunc28Mul8(byte[] sk)
    {
        ArgumentNullException.ThrowIfNull(sk);

        if (sk.Length != 32)
        {
            throw new ArgumentException("sk is incorrect length", nameof(sk));
        }

        return PointOfTrunc28Mul8(sk.AsSpan());
    }

    internal static byte[] PointOfTrunc28Mul8(ReadOnlySpan<byte> sk)
    {
        byte[] scalar = new byte[32];
        Add28Mul8(scalar, sk, scalar);
        return Ed25519.GetPublicKey(scalar);
    }

    internal static void DeriveChildHmac(
        ReadOnlySpan<byte> material,
        ReadOnlySpan<byte> chaincode,
        uint index,
        Span<byte> zDestination,
        Span<byte> chaincodeDestination)
    {
        bool isHard = FromIndex(index) == DerivationType.HARD;
        Span<byte> input = stackalloc byte[69];
        Span<byte> message = input[..(1 + material.Length + 4)];
        Span<byte> iOutput = stackalloc byte[64];
        try
        {
            message[0] = isHard ? (byte)0x00 : (byte)0x02;
            material.CopyTo(message[1..]);
            BinaryPrimitives.WriteUInt32LittleEndian(message[^4..], index);
            _ = HMACSHA512.HashData(chaincode, message, zDestination);

            message[0] = isHard ? (byte)0x01 : (byte)0x03;
            _ = HMACSHA512.HashData(chaincode, message, iOutput);
            iOutput[32..].CopyTo(chaincodeDestination);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(message);
            CryptographicOperations.ZeroMemory(iOutput);
        }
    }

    /// <summary>
    /// Converts a 32-bit integer to little-endian byte array format.
    /// </summary>
    /// <param name="i">The integer value to convert.</param>
    /// <returns>A 4-byte little-endian representation.</returns>
    public static byte[] Le32(ulong i) => [(byte)i, (byte)(i >> 8), (byte)(i >> 16), (byte)(i >> 24)];

    /// <summary>
    /// Validates whether a string is a canonical BIP-32 derivation path starting with "m/" and one or more non-negative ASCII-digit indices.
    /// </summary>
    /// <param name="path">The derivation path string (e.g., "m/1852'/1815'/0'").</param>
    /// <returns>True if the path is valid; otherwise false.</returns>
    public static bool IsValidPath(string path)
    {
        ArgumentNullException.ThrowIfNull(path);

        if (!path.StartsWith("m/", StringComparison.Ordinal) || path.Length == 2)
        {
            return false;
        }

        ReadOnlySpan<char> remaining = path.AsSpan(2);
        while (true)
        {
            int slash = remaining.IndexOf('/');
            ReadOnlySpan<char> segment = slash < 0 ? remaining : remaining[..slash];
            if (segment.Length > 0 && segment[^1] == '\'')
            {
                segment = segment[..^1];
            }

            if (segment.Length == 0 || (segment.Length > 1 && segment[0] == '0'))
            {
                return false;
            }

            foreach (char c in segment)
            {
                if (c is < '0' or > '9')
                {
                    return false;
                }
            }

            if (!int.TryParse(segment, NumberStyles.None, CultureInfo.InvariantCulture, out _))
            {
                return false;
            }

            if (slash < 0)
            {
                return true;
            }

            remaining = remaining[(slash + 1)..];
        }
    }

    /// <summary>
    /// Determines the derivation type (hard or soft) from a derivation index.
    /// </summary>
    /// <param name="index">The derivation index.</param>
    /// <returns>The derivation type based on whether the index is hardened.</returns>
    public static DerivationType FromIndex(ulong index) => index >= FirstHardenedIndex ? DerivationType.HARD : DerivationType.SOFT;
}
