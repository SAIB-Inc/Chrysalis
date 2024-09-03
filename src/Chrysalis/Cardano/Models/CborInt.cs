using Chrysalis.Cbor;

namespace Chrysalis.Cardano.Models;

[CborSerializable(CborType.Int)]
public record CborInt(int Value) : ICbor;