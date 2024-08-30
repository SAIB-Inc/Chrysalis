using Chrysalis.Cbor;

namespace Chrysalis.Cbor;
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct)]
public class CborSerializableAttribute(CborType type) : Attribute
{
    public CborType Type { get; } = type;
    public int Index { get; set; } = -1;
    public bool IsIndefinite { get; set; } = false;

    public CborSerializableAttribute(CborType type, int index) : this(type)
    {
        Index = index;
    }

    public CborSerializableAttribute(CborType type, bool isIndefinite) : this(type)
    {
        IsIndefinite = isIndefinite;
    }

    public CborSerializableAttribute(CborType type, int index, bool isIndefinite) : this(type)
    {
        Index = index;
        IsIndefinite = isIndefinite;
    }
}
