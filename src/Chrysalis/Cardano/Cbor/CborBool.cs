using Chrysalis.Cbor;

namespace Chrysalis.Cardano.Cbor;

[CborSerializable(CborType.Bool)]
public record CborBool(bool Value = false) : RawCbor
{
    public CborBool() : this(false) { }
}


    