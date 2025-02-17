using Chrysalis.Cbor.Attributes;
using Chrysalis.Cbor.Serialization.Converters.Custom;
using Chrysalis.Cbor.Types;

namespace Chrysalis.Cbor.Cardano.Types.Block.Transaction.Script;

[CborConverter(typeof(UnionConverter))]
public abstract record PlutusOption<T> : CborBase;

public record PlutusSome<T>([CborIndex(0)] T Value) : PlutusOption<T>;

public record PlutusNone<T> : PlutusOption<T>;