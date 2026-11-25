// Contains code adapted from Chaos.NaCl (public domain); changed for Chrysalis. See THIRD-PARTY-NOTICES.md.
namespace Chrysalis.Crypto.Internal.Ed25519Ref10;

/// <summary>
/// Group element operations for Ed25519.
/// </summary>
internal static partial class GroupOperations
{
    public static void ge_p3_tobytes(Span<byte> s, int offset, ref GroupElementP3 h)
    {
        ge_p3_to_p2(out GroupElementP2 p2, ref h);
        ge_tobytes(s.Slice(offset, 32), ref p2);
    }
}