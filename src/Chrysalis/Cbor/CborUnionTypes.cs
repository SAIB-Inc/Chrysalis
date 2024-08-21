namespace Chrysalis.Cbor;

[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct, AllowMultiple = false)]
public class CborUnionTypesAttribute : Attribute
{
    public Type[] UnionTypes { get; }

    public CborUnionTypesAttribute(params Type[] unionTypes)
    {
        UnionTypes = unionTypes;
    }
}
