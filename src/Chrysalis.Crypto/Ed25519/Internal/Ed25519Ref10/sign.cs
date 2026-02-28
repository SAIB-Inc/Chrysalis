using Chrysalis.Crypto;

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
        byte[] az;
        byte[] r;
        byte[] hram;
        GroupElementP3 R;
        Sha512 hasher = new();
        {
            hasher.Update(sk, skoffset, 32);
            az = hasher.Finish();
            ScalarOperations.sc_clamp(az, 0);

            hasher.Init();
            hasher.Update(az, 32, 32);
            hasher.Update(m, moffset, mlen);
            r = hasher.Finish();

            ScalarOperations.sc_reduce(r);
            GroupOperations.ge_scalarmult_base(out R, r, 0);
            GroupOperations.ge_p3_tobytes(sig, sigoffset, ref R);

            hasher.Init();
            hasher.Update(sig, sigoffset, 32);
            hasher.Update(sk, skoffset + 32, 32);
            hasher.Update(m, moffset, mlen);
            hram = hasher.Finish();

            ScalarOperations.sc_reduce(hram);
            byte[] s = new byte[32];
            Array.Copy(sig, sigoffset + 32, s, 0, 32);
            ScalarOperations.sc_muladd(s, hram, az, r);
            Array.Copy(s, 0, sig, sigoffset + 32, 32);
            CryptoBytes.Wipe(s);
        }
    }

    internal static void crypto_sign3(
        byte[] sig, byte[] m, byte[] sk)
    {
        byte[] r;
        byte[] hram;
        GroupElementP3 R;
        GroupElementP3 A;
        Sha512 hasher = new();
        {
            hasher.Init();
            hasher.Update(sk, 32, 32);
            hasher.Update(m, 0, m.Length);
            r = hasher.Finish();
            ScalarOperations.sc_reduce(r);

            byte[] s1 = new byte[32];
            GroupOperations.ge_scalarmult_base(out R, r, 0);
            GroupOperations.ge_p3_tobytes(s1, 0, ref R);
            Array.Copy(s1, 0, sig, 0, 32);

            byte[] pk = new byte[32];
            GroupOperations.ge_scalarmult_base(out A, sk, 0);
            GroupOperations.ge_p3_tobytes(pk, 0, ref A);
            Array.Copy(pk, 0, sig, 32, 32);

            hasher.Init();
            hasher.Update(sig, 0, sig.Length);
            hasher.Update(m, 0, m.Length);
            hram = hasher.Finish();

            ScalarOperations.sc_reduce(hram);
            byte[] s2 = new byte[32];
            byte[] sk1 = new byte[32];
            byte[] r1 = new byte[32];
            byte[] hram1 = new byte[32];
            Array.Copy(sig, 32, s2, 0, 32);
            Array.Copy(hram, 0, hram1, 0, 32);
            Array.Copy(sk, 0, sk1, 0, 32);
            Array.Copy(r, 0, r1, 0, 32);

            ScalarOperations.sc_muladd(s2, hram1, sk1, r1);
            Array.Copy(s2, 0, sig, 32, 32);
            CryptoBytes.Wipe(r1);
            CryptoBytes.Wipe(s1);
            CryptoBytes.Wipe(s2);
            CryptoBytes.Wipe(sk1);
            CryptoBytes.Wipe(hram1);
        }
    }
}
