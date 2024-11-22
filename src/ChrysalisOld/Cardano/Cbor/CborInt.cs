using Chrysalis.Cbor;

namespace Chrysalis.Cardano.Cbor;

[CborSerializable(CborType.Int)]
public record CborInt(int Value) : RawCbor;