namespace Chrysalis.Cbor;

[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct | AttributeTargets.Interface)]
public class CborSerializableAttribute(CborType type) : Attribute
{
    public CborType Type { get; } = type;
    public int Index { get; set; } = -1;
    public bool IsDefinite { get; set; } = true;
    public bool IsCustom { get; set; } = false;

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
