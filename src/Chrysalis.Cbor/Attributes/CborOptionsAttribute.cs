namespace Chrysalis.Cbor.Attributes;

/// <summary>
/// Defines CBOR-specific options for a type
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct | AttributeTargets.Interface)]
public sealed class CborOptionsAttribute : Attribute
{
    public int Tag { get; init; } = -1;
    public int Index { get; init; } = -1;
    public bool IsDefinite { get; init; } = true;
    public int Size { get; init; } = -1;
}