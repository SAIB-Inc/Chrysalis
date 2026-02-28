namespace Chrysalis.Crypto.Internal.Ed25519Ref10;

/// <summary>
/// Scalar arithmetic operations for Ed25519.
/// </summary>
internal static partial class ScalarOperations
{
        public static void sc_clamp(byte[] s, int offset)
        {
            s[offset + 0] &= 248;
            s[offset + 31] &= 127;
            s[offset + 31] |= 64;
        }
}