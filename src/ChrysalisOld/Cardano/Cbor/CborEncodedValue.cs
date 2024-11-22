using Chrysalis.Cbor;

namespace Chrysalis.Cardano.Cbor;

[CborSerializable(CborType.EncodedValue)]
public record CborEncodedValue(byte[] Value) : RawCbor;