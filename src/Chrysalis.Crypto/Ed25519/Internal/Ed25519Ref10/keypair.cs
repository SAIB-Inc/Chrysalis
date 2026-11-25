// Contains code adapted from Chaos.NaCl (public domain); changed for Chrysalis. See THIRD-PARTY-NOTICES.md.
using System.Security.Cryptography;

namespace Chrysalis.Crypto.Internal.Ed25519Ref10;

/// <summary>
/// Ed25519 key pair and public key generation operations.
/// </summary>
internal static partial class Ed25519Operations
{
    internal static void crypto_sign_keypair(byte[] pk, int pkoffset, byte[] sk, int skoffset, byte[] seed, int seedoffset)
    {
        GroupElementP3 A;

        Array.Copy(seed, seedoffset, sk, skoffset, 32);
        byte[] h = SHA512.HashData(sk.AsSpan(skoffset, 32));
        try
        {
            ScalarOperations.sc_clamp(h, 0);

            GroupOperations.ge_scalarmult_base(out A, h, 0);
            GroupOperations.ge_p3_tobytes(pk, pkoffset, ref A);

            for (int i = 0; i < 32; ++i) sk[skoffset + 32 + i] = pk[pkoffset + i];
        }
        finally
        {
            CryptographicOperations.ZeroMemory(h);
        }
    }

    internal static void crypto_get_pubkey(byte[] pk, byte[] sk)
    {
        GroupElementP3 A;

        GroupOperations.ge_scalarmult_base(out A, sk, 0);
        GroupOperations.ge_p3_tobytes(pk, 0, ref A);
    }

    internal static int crypto_point_plus(byte[] p1, byte[] p2, byte[] s)
    {
        if (!TryDecodeNegatedCanonicalPoint(p1, 0, out GroupElementP3 A))
        {
            return 1;
        }

        if (!TryDecodeNegatedCanonicalPoint(p2, 0, out GroupElementP3 B))
        {
            return 2;
        }

        GroupOperations.ge_p3_to_cached(out GroupElementCached Bc, ref B);
        GroupOperations.ge_add(out GroupElementP1P1 P, ref A, ref Bc);
        GroupOperations.ge_p1p1_to_p3(out GroupElementP3 R, ref P);
        FieldOperations.fe_neg(out R.X, ref R.X);
        GroupOperations.ge_p3_tobytes(s, 0, ref R);
        return 0;
    }

    private static bool TryDecodeNegatedCanonicalPoint(byte[] data, int offset, out GroupElementP3 h)
    {
        if (!IsCanonicalY(data.AsSpan(offset, 32)) || GroupOperations.ge_frombytes_negate_vartime(out h, data, offset) != 0)
        {
            h = default;
            return false;
        }

        if ((data[offset + 31] >> 7) != 0 && FieldOperations.fe_isnonzero(ref h.X) == 0)
        {
            h = default;
            return false;
        }

        return true;
    }

    private static bool IsCanonicalY(ReadOnlySpan<byte> data)
    {
        if ((data[31] & 0x7F) != 0x7F)
        {
            return true;
        }

        for (int i = 1; i < 31; i++)
        {
            if (data[i] != 0xFF)
            {
                return true;
            }
        }

        return data[0] < 0xED;
    }
}
