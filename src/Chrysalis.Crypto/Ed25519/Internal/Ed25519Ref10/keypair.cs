// Contains code adapted from Chaos.NaCl (public domain); changed for Chrysalis. See THIRD-PARTY-NOTICES.md.
using System.Security.Cryptography;

namespace Chrysalis.Crypto.Internal.Ed25519Ref10;

/// <summary>
/// Ed25519 key pair and public key generation operations.
/// </summary>
internal static partial class Ed25519Operations
{
    internal static void crypto_sign_keypair(Span<byte> pk, Span<byte> sk, ReadOnlySpan<byte> seed)
    {
        GroupElementP3 A;

        seed[..32].CopyTo(sk);
        Span<byte> h = stackalloc byte[64];
        try
        {
            _ = SHA512.HashData(sk[..32], h);
            ScalarOperations.sc_clamp(h, 0);

            GroupOperations.ge_scalarmult_base(out A, h, 0);
            GroupOperations.ge_p3_tobytes(pk, 0, ref A);

            pk[..32].CopyTo(sk[32..]);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(h);
        }
    }

    internal static void crypto_get_pubkey(Span<byte> pk, ReadOnlySpan<byte> sk)
    {
        GroupElementP3 A;

        GroupOperations.ge_scalarmult_base(out A, sk, 0);
        GroupOperations.ge_p3_tobytes(pk, 0, ref A);
    }

    internal static int crypto_point_plus(ReadOnlySpan<byte> p1, ReadOnlySpan<byte> p2, Span<byte> s)
    {
        if (!TryDecodeNegatedCanonicalPoint(p1, out GroupElementP3 A))
        {
            return 1;
        }

        if (!TryDecodeNegatedCanonicalPoint(p2, out GroupElementP3 B))
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

    private static bool TryDecodeNegatedCanonicalPoint(ReadOnlySpan<byte> data, out GroupElementP3 h)
    {
        if (!IsCanonicalY(data[..32]) || GroupOperations.ge_frombytes_negate_vartime(out h, data, 0) != 0)
        {
            h = default;
            return false;
        }

        if ((data[31] >> 7) != 0 && FieldOperations.fe_isnonzero(ref h.X) == 0)
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
