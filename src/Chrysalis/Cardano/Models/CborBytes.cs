using Chrysalis.Cbor;

namespace Chrysalis.Cardano.Models;

[CborSerializable(CborType.Bytes)]
public record CborBytes(byte[] Value) : ICbor;