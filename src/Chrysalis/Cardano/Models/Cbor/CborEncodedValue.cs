using Chrysalis.Cbor;

namespace Chrysalis.Cardano.Models.Cbor;

[CborSerializable(CborType.EncodedValue)]
public record CborEncodedValue(byte[] Value) : RawCbor;