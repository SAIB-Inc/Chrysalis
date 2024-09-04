using Chrysalis.Cbor;

namespace Chrysalis.Cbor;
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct)]
public class CborSerializableAttribute(CborType type) : Attribute
{
    public CborType Type { get; } = type;
    public int Index { get; set; } = -1;
    public bool IsDefinite { get; set; } = true;

    public CborSerializableAttribute(CborType type, int index) : this(type)
    {
        Index = index;
    }

    public CborSerializableAttribute(CborType type, bool isDefinite) : this(type)
    {
        IsDefinite = isDefinite;
    }

    public CborSerializableAttribute(CborType type, int index, bool isDefinite) : this(type)
    {
        Index = index;
        IsDefinite = isDefinite;
    }
}
