using Chrysalis.Cbor;

namespace Chrysalis.Cardano.Models.Cbor;

[CborSerializable(CborType.Bytes)]
public record CborBytes(byte[] Value) : ICbor;