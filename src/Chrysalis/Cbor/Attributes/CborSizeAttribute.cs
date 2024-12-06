namespace Chrysalis.Cbor.Attributes;

[AttributeUsage(AttributeTargets.Class | AttributeTargets.Property)]
public class CborSizeAttribute(int size) : Attribute
{
    public int Size { get; } = size;
}