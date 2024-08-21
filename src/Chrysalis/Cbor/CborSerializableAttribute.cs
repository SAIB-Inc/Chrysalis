using Chrysalis.Cbor;

[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct)]
public class CborSerializableAttribute : Attribute
{
    public CborType Type { get; }
    public int Index { get; set; }

    public CborSerializableAttribute(CborType Type)
    {
        this.Type = Type;
        Index = 0;
    }

    public CborSerializableAttribute(CborType Type, int Index)
    {
        this.Type = Type;
        this.Index = Index;
    }
}
