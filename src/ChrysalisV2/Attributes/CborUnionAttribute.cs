namespace ChrysalisV2.Attributes;

[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct | AttributeTargets.Interface, AllowMultiple = false)]
public class CborUnionAttribute(params Type[] unionTypes) : Attribute
{
    public Type[] UnionTypes { get; } = unionTypes;
}