// Contains code adapted from Chaos.NaCl (public domain); changed for Chrysalis. See THIRD-PARTY-NOTICES.md.
using System.Security.Cryptography;

namespace Chrysalis.Crypto.Internal.Ed25519Ref10;

/// <summary>
/// Ed25519 signing operations.
/// </summary>
internal static partial class Ed25519Operations
{
    internal static void crypto_sign2(
        byte[] sig, int sigoffset,
        byte[] m, int moffset, int mlen,
        byte[] sk, int skoffset)
    {
        byte[] az = new byte[64];
        try
        {
            _ = SHA512.HashData(sk.AsSpan(skoffset, 32), az);
            ScalarOperations.sc_clamp(az, 0);
            SignCore(sig, sigoffset, m.AsSpan(moffset, mlen), az, az.AsSpan(32, 32), sk.AsSpan(skoffset + 32, 32));
        }
        finally
        {
            CryptographicOperations.ZeroMemory(az);
        }
    }

    internal static void crypto_sign3(
        byte[] sig, byte[] m, byte[] sk)
    {
        byte[] pk = new byte[32];
        crypto_get_pubkey(pk, sk);
        SignCore(sig, 0, m, sk, sk.AsSpan(32, 32), pk);
    }

    private static void SignCore(
        byte[] sig, int sigoffset,
        ReadOnlySpan<byte> m,
        byte[] scalar,
        ReadOnlySpan<byte> prefix,
        ReadOnlySpan<byte> publicKey)
    {
        byte[] r = new byte[64];
        byte[] hram = new byte[64];
        byte[] s = new byte[32];
        try
        {
            HashParts(prefix, ReadOnlySpan<byte>.Empty, m, r, wipeBuffer: true);
            ScalarOperations.sc_reduce(r);

            GroupOperations.ge_scalarmult_base(out GroupElementP3 R, r, 0);
            GroupOperations.ge_p3_tobytes(sig, sigoffset, ref R);

            HashParts(sig.AsSpan(sigoffset, 32), publicKey, m, hram, wipeBuffer: false);
            ScalarOperations.sc_reduce(hram);

            ScalarOperations.sc_muladd(s, hram, scalar, r);
            Array.Copy(s, 0, sig, sigoffset + 32, 32);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(r);
            CryptographicOperations.ZeroMemory(hram);
            CryptographicOperations.ZeroMemory(s);
        }
    }
}
