using Chrysalis.Cbor.Attributes;
using Chrysalis.Cbor.Converters.Primitives;
using Chrysalis.Cbor.Types;
using Chrysalis.Cbor.Types.Primitives;

namespace Chrysalis.Cardano.Core.Types.Block.Transaction.Output;

[CborConverter(typeof(UnionConverter))]
public abstract record MultiAsset : CborBase;


[CborConverter(typeof(MapConverter))]
[CborDefinite]
public record MultiAssetOutput(
    Dictionary<CborBytes, TokenBundleOutput> Value
) : MultiAsset;


[CborConverter(typeof(MapConverter))]
[CborDefinite]
public record MultiAssetMint(
    Dictionary<CborBytes, TokenBundleMint> Value
) : MultiAsset;