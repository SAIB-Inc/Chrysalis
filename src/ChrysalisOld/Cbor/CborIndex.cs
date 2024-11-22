namespace Chrysalis.Cbor;

public class CborIndex(CborType type, object value)
{
    public CborType Type { get; } = type;
    public object Value { get; } = value;
}