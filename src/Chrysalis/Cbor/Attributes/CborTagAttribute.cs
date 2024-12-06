namespace Chrysalis.Cbor.Attributes;

[AttributeUsage(AttributeTargets.Class | AttributeTargets.Interface)]
public class CborTagAttribute(int Tag) : Attribute
{
    public int Tag { get; } = Tag;
}