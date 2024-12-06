namespace Chrysalis.Cbor.Attributes;

[AttributeUsage(AttributeTargets.Class)]
public class CborIndexAttribute(int tag) : Attribute
{
    public int Index { get; } = tag;
}