using Chrysalis.Cbor;

namespace Chrysalis.Cbor;
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct)]
public class CborSerializableAttribute(CborType type) : Attribute
{
    public CborType Type { get; } = type;
    public int Index { get; set; } = -1;

    public CborSerializableAttribute(CborType type, int index) : this(type)
    {
        Index = index;
    }
}
