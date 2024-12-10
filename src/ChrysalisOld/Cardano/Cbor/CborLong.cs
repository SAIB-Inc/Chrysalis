using Chrysalis.Cbor;

namespace Chrysalis.Cardano.Cbor;

[CborSerializable(CborType.Long)]
public record CborLong(long Value) : RawCbor;