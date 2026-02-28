using Chrysalis.Crypto;

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
        byte[] h = Sha512.Hash(sk, skoffset, 32);
        ScalarOperations.sc_clamp(h, 0);

        GroupOperations.ge_scalarmult_base(out A, h, 0);
        GroupOperations.ge_p3_tobytes(pk, pkoffset, ref A);

        for (int i = 0; i < 32; ++i) sk[skoffset + 32 + i] = pk[pkoffset + i];
        CryptoBytes.Wipe(h);
    }

    internal static void crypto_get_pubkey(byte[] pk, byte[] sk)
    {
        GroupElementP3 A;

        GroupOperations.ge_scalarmult_base(out A, sk, 0);
        GroupOperations.ge_p3_tobytes(pk, 0, ref A);
    }

    internal static void crypto_point_plus(byte[] p1, byte[] p2, byte[] s)
    {
        GroupOperations.ge_frombytes_negate_vartime(out GroupElementP3 A, p1, 0);
        GroupOperations.ge_frombytes_negate_vartime(out GroupElementP3 B, p2, 0);
        GroupOperations.ge_p3_to_cached(out GroupElementCached Bc, ref B);
        GroupOperations.ge_add(out GroupElementP1P1 P, ref A, ref Bc);
        GroupOperations.ge_p1p1_to_p3(out GroupElementP3 R, ref P);
        GroupOperations.ge_p3_tobytes(s, 0, ref R);
        s[31] ^= 0x80;
    }
}
