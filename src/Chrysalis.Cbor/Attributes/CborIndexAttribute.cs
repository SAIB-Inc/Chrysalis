namespace Chrysalis.Cbor.Attributes;

/// <summary>
/// Maps a property to a CBOR array index for list/array types
/// </summary>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Parameter)]
public class CborIndexAttribute(int index) : Attribute
{
    public int Index { get; } = index;
}