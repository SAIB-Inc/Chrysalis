namespace Chrysalis.Cbor.Attributes;

[AttributeUsage(AttributeTargets.Property | AttributeTargets.Parameter)]
public class CborPropertyAttribute(int index) : Attribute
{
    public int Index { get; } = index;
}