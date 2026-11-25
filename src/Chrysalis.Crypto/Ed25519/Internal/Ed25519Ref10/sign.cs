// Contains code adapted from Chaos.NaCl (public domain); changed for Chrysalis. See THIRD-PARTY-NOTICES.md.
using System.Security.Cryptography;

namespace Chrysalis.Crypto.Internal.Ed25519Ref10;

/// <summary>
/// Ed25519 signing operations.
/// </summary>
internal static partial class Ed25519Operations
{
    internal static void crypto_sign2(
        Span<byte> sig,
        ReadOnlySpan<byte> m,
        ReadOnlySpan<byte> sk)
    {
        Span<byte> az = stackalloc byte[64];
        try
        {
            _ = SHA512.HashData(sk[..32], az);
            ScalarOperations.sc_clamp(az, 0);
            SignCore(sig, m, az, az.Slice(32, 32), sk.Slice(32, 32));
        }
        finally
        {
            CryptographicOperations.ZeroMemory(az);
        }
    }

    internal static void crypto_sign3(
        Span<byte> sig, ReadOnlySpan<byte> m, ReadOnlySpan<byte> sk)
    {
        Span<byte> pk = stackalloc byte[32];
        try
        {
            crypto_get_pubkey(pk, sk);
            SignCore(sig, m, sk, sk.Slice(32, 32), pk);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(pk);
        }
    }

    private static void SignCore(
        Span<byte> sig,
        ReadOnlySpan<byte> m,
        ReadOnlySpan<byte> scalar,
        ReadOnlySpan<byte> prefix,
        ReadOnlySpan<byte> publicKey)
    {
        Span<byte> r = stackalloc byte[64];
        Span<byte> hram = stackalloc byte[64];
        Span<byte> s = stackalloc byte[32];
        try
        {
            HashParts(prefix, ReadOnlySpan<byte>.Empty, m, r, wipeBuffer: true);
            ScalarOperations.sc_reduce(r);

            GroupOperations.ge_scalarmult_base(out GroupElementP3 R, r, 0);
            GroupOperations.ge_p3_tobytes(sig, 0, ref R);

            HashParts(sig[..32], publicKey, m, hram, wipeBuffer: false);
            ScalarOperations.sc_reduce(hram);

            ScalarOperations.sc_muladd(s, hram, scalar, r);
            s.CopyTo(sig[32..]);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(r);
            CryptographicOperations.ZeroMemory(hram);
            CryptographicOperations.ZeroMemory(s);
        }
    }
}
