namespace Chrysalis.Crypto.Internal.Ed25519Ref10;

/// <summary>
/// Group element operations for Ed25519.
/// </summary>
internal static partial class GroupOperations
{
		public static void ge_tobytes(byte[] s, int offset, ref  GroupElementP2 h)
		{
			FieldElement recip;
			FieldElement x;
			FieldElement y;

			FieldOperations.fe_invert(out recip, ref h.Z);
			FieldOperations.fe_mul(out x, ref h.X, ref recip);
			FieldOperations.fe_mul(out y, ref h.Y, ref recip);
			FieldOperations.fe_tobytes(s, offset, ref y);
			s[offset + 31] ^= (byte)(FieldOperations.fe_isnegative(ref x) << 7);
		}
}