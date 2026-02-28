namespace Chrysalis.Crypto.Internal.Ed25519Ref10;

/// <summary>
/// Field element operations for Ed25519.
/// </summary>
internal static partial class FieldOperations
{
    /// <summary>
    /// Sets a field element to one.
    /// </summary>
    internal static void fe_1(out FieldElement h)
    {
        h = default;
        h.x0 = 1;
    }
}
