using Chrysalis.Cbor;

namespace Chrysalis.Cardano.Models.Script;

[CborSerializable(CborType.Union)]
[CborUnionTypes([typeof(PlutusSome<>), typeof(PlutusNone<>)])]
public record PlutusOption<T> : RawCbor;

public record PlutusSome<T>([CborProperty(0)] T Value) : PlutusOption<T>;

public record PlutusNone<T> : PlutusOption<T>;