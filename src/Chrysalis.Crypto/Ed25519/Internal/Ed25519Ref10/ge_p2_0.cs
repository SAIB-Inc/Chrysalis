namespace Chrysalis.Crypto.Internal.Ed25519Ref10;

/// <summary>
/// Group element operations for Ed25519.
/// </summary>
internal static partial class GroupOperations
{
		public static void ge_p2_0(out  GroupElementP2 h)
		{
			FieldOperations.fe_0(out h.X);
			FieldOperations.fe_1(out h.Y);
			FieldOperations.fe_1(out h.Z);
		}
}