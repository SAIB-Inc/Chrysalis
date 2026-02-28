namespace Chrysalis.Crypto.Internal.Ed25519Ref10;

/// <summary>
/// Group element operations for Ed25519.
/// </summary>
internal static partial class GroupOperations
{
		public static void ge_precomp_0(out GroupElementPreComp h)
		{
			FieldOperations.fe_1(out h.yplusx);
			FieldOperations.fe_1(out h.yminusx);
			FieldOperations.fe_0(out h.xy2d);
		}
}