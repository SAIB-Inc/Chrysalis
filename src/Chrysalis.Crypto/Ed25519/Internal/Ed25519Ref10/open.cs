// Contains code adapted from Chaos.NaCl (public domain); changed for Chrysalis. See THIRD-PARTY-NOTICES.md.
using System.Security.Cryptography;

namespace Chrysalis.Crypto.Internal.Ed25519Ref10;

/// <summary>
/// Ed25519 signature verification operations.
/// </summary>
internal static partial class Ed25519Operations
{
    private const int StackMessageLimit = 960;

    internal static bool crypto_sign_verify(
        ReadOnlySpan<byte> sig,
        ReadOnlySpan<byte> m,
        ReadOnlySpan<byte> pk)
    {
        ReadOnlySpan<byte> rBytes = sig[..32];
        ReadOnlySpan<byte> sBytes = sig.Slice(32, 32);

        if (!ScalarOperations.sc_is_canonical(sBytes))
        {
            return false;
        }

        if (!TryDecodeNegatedCanonicalPoint(pk, out GroupElementP3 A))
        {
            return false;
        }

        if (HasSmallOrder(ref A.X, ref A.Y))
        {
            return false;
        }

        Span<byte> h = stackalloc byte[64];
        HashParts(rBytes, pk[..32], m, h, wipeBuffer: false);

        ScalarOperations.sc_reduce(h);

        GroupOperations.ge_double_scalarmult_vartime(out GroupElementP2 R, h[..32], ref A, sBytes);
        if (HasSmallOrder(ref R.X, ref R.Y))
        {
            return false;
        }

        Span<byte> checkr = stackalloc byte[32];
        GroupOperations.ge_tobytes(checkr, ref R);
        return CryptographicOperations.FixedTimeEquals(checkr, rBytes);
    }

    internal static void HashParts(
        ReadOnlySpan<byte> part1,
        ReadOnlySpan<byte> part2,
        ReadOnlySpan<byte> message,
        Span<byte> destination,
        bool wipeBuffer)
    {
        int prefixLen = part1.Length + part2.Length;
        if (prefixLen <= 64 && message.Length <= StackMessageLimit)
        {
            Span<byte> buffer = stackalloc byte[64 + StackMessageLimit];
            int total = prefixLen + message.Length;
            try
            {
                part1.CopyTo(buffer);
                part2.CopyTo(buffer[part1.Length..]);
                message.CopyTo(buffer[prefixLen..]);
                _ = SHA512.HashData(buffer[..total], destination);
            }
            finally
            {
                if (wipeBuffer)
                {
                    CryptographicOperations.ZeroMemory(buffer[..total]);
                }
            }
        }
        else
        {
            using IncrementalHash hasher = IncrementalHash.CreateHash(HashAlgorithmName.SHA512);
            hasher.AppendData(part1);
            if (!part2.IsEmpty)
            {
                hasher.AppendData(part2);
            }

            hasher.AppendData(message);
            _ = hasher.GetHashAndReset(destination);
        }
    }

    internal static bool HasSmallOrder(ref FieldElement x, ref FieldElement y)
    {
        if (FieldOperations.fe_isnonzero(ref x) == 0 || FieldOperations.fe_isnonzero(ref y) == 0)
        {
            return true;
        }

        FieldOperations.fe_mul(out FieldElement ySqrtM1, ref y, ref LookupTables.sqrtm1);
        FieldOperations.fe_sub(out FieldElement diff, ref ySqrtM1, ref x);
        if (FieldOperations.fe_isnonzero(ref diff) == 0)
        {
            return true;
        }

        FieldOperations.fe_add(out FieldElement sum, ref ySqrtM1, ref x);
        return FieldOperations.fe_isnonzero(ref sum) == 0;
    }
}
