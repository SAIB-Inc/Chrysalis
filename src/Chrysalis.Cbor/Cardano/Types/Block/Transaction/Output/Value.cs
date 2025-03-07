using Chrysalis.Cbor.Attributes;
using Chrysalis.Cbor.Serialization.Converters.Custom;
using Chrysalis.Cbor.Serialization.Converters.Primitives;
using Chrysalis.Cbor.Types;

namespace Chrysalis.Cbor.Cardano.Types.Block.Transaction.Output;

[CborConverter(typeof(UnionConverter))]
public abstract record Value : CborBase;


[CborConverter(typeof(UlongConverter))]
public record Lovelace(ulong Value) : Value;


[CborConverter(typeof(CustomListConverter))]
[CborOptions(IsDefinite = true)]
public record LovelaceWithMultiAsset(
    [CborIndex(0)] Lovelace Lovelace,
    [CborIndex(1)] MultiAssetOutput MultiAsset
) : Value;