using Chrysalis.Cbor;

namespace Chrysalis.Cardano.Core;

[CborSerializable(CborType.Nullable)]
public record CborNullable<T> : RawCbor where T : ICbor
{
    public T? Value { get; set; }
    public CborNullable() { }

    public CborNullable(T? value)
    {
        Value = value;
    }
}