using Chrysalis.Cbor;

namespace Chrysalis.Cardano.Models.Plutus;

[CborSerializable(CborType.Union)]
[CborUnionTypes([typeof(Some<>), typeof(None<>)])]
public record Option<T> : ICbor;

[CborSerializable(CborType.Constr, Index = 0)]
public record Some<T>([CborProperty(0)] T Value) : Option<T>;

[CborSerializable(CborType.Constr, Index = 1)]
public record None<T> : Option<T>;