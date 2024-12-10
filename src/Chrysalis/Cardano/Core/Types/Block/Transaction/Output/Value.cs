using Chrysalis.Cbor.Attributes;
using Chrysalis.Cbor.Converters.Primitives;
using Chrysalis.Cbor.Types;

namespace Chrysalis.Cardano.Core.Types.Block.Transaction.Output;


[CborConverter(typeof(UnionConverter))]
public abstract record Value : CborBase;


[CborConverter(typeof(UlongConverter))]
public record Lovelace(ulong Value) : Value;


[CborConverter(typeof(CustomListConverter))]
public record LovelaceWithMultiAsset(
    [CborProperty(0)] Lovelace Lovelace,
    [CborProperty(1)] MultiAssetOutput MultiAsset
) : Value;