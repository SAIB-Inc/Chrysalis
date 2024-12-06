using Chrysalis.Cbor.Attributes;
using Chrysalis.Cbor.Converters.Primitives;
using Chrysalis.Cbor.Types;

namespace Chrysalis.Cardano.Core.Types.Block.Transaction.Script;

[CborConverter(typeof(UnionConverter))]
public abstract record PlutusOption<T> : CborBase;

public record PlutusSome<T>([CborProperty(0)] T Value) : PlutusOption<T>;

public record PlutusNone<T> : PlutusOption<T>;