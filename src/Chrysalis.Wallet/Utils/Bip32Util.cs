using Chaos.NaCl;
using Chrysalis.Wallet.Extensions;
using Chrysalis.Wallet.Models.Enums;

namespace Chrysalis.Wallet.Utils;

public static class Bip32Util
{
    // This function correctly implements the scalar addition with a 28-byte truncation and multiplication by 8, 
    // which is part of Cardano's derivation scheme.
    public static byte[] Add28Mul8(byte[] x, byte[] y)
    {
        if (x.Length != 32)
            throw new Exception("x is incorrect length");
        if (y.Length != 32)
            throw new Exception("y is incorrect length");

        ushort carry = 0;
        var res = new byte[32];

        for (var i = 0; i < 28; i++)
        {
            var r = (ushort)x[i] + (((ushort)y[i]) << 3) + carry;
            res[i] = (byte)(r & 0xff);
            carry = (ushort)(r >> 8);
        }

        for (var j = 28; j < 32; j++)
        {
            var r = (ushort)x[j] + carry;
            res[j] = (byte)(r & 0xff);
            carry = (ushort)(r >> 8);
        }

        return res;
    }
    // This handles 256-bit addition with carry, which is needed for the right half of the key derivation.
    public static byte[] Add256Bits(byte[] x, byte[] y)
    {
        if (x.Length != 32)
            throw new Exception("x is incorrect length");
        if (y.Length != 32)
            throw new Exception("y is incorrect length");

        ushort carry = 0;
        var res = new byte[32];

        for (var i = 0; i < 32; i++)
        {
            var r = x[i] + y[i] + carry;
            res[i] = (byte)(r);
            carry = (ushort)(r >> 8);
        }

        return res;
    }
    
    public static byte[] PointOfTrunc28Mul8(byte[] sk)
    {
        var kl = new byte[32];
        var copy = Add28Mul8(kl, sk);
        return Ed25519.GetPublicKey(copy);
    }

    // Correctly converts a 32-bit integer to little-endian byte array format.
    public static byte[] Le32(ulong i) => [(byte)i, (byte)(i >> 8), (byte)(i >> 16), (byte)(i >> 24)];

    public static bool IsValidPath(string path) => 
        !path.Split('/').Skip(1).Select(a => a.Replace("'", "")).Any(a => !int.TryParse(a, out _));

    public static DerivationType FromIndex(ulong index) => index >= 0x80000000 ? DerivationType.HARD : DerivationType.SOFT;
}