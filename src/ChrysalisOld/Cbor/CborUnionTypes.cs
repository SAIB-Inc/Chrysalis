namespace Chrysalis.Cbor;

[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct | AttributeTargets.Interface, AllowMultiple = false)]
public class CborUnionTypesAttribute(params Type[] unionTypes) : Attribute
{
    public Type[] UnionTypes { get; } = unionTypes;
}
