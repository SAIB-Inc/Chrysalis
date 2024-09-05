using Chrysalis.Cbor;

namespace Chrysalis.Cardano.Models.Plutus;

[CborSerializable(CborType.Constr, Index = 1)]
public record None<T> : Option<T>;