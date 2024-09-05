using Chrysalis.Cbor;

namespace Chrysalis.Cardano.Models.Cbor;

[CborSerializable(CborType.Int)]
public record CborInt(int Value) : ICbor;