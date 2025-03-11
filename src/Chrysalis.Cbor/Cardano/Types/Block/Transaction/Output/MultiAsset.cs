using Chrysalis.Cbor.Attributes;
using Chrysalis.Cbor.Serialization.Converters.Custom;
using Chrysalis.Cbor.Serialization.Converters.Primitives;
using Chrysalis.Cbor.Types;
using Chrysalis.Cbor.Types.Primitives;

namespace Chrysalis.Cbor.Cardano.Types.Block.Transaction.Output;

[CborConverter(typeof(UnionConverter))]
public abstract partial record MultiAsset : CborBase;


[CborConverter(typeof(MapConverter))]
[CborOptions(IsDefinite = true)]
public partial record MultiAssetOutput(
    Dictionary<CborBytes, TokenBundleOutput> Value
) : MultiAsset;


[CborConverter(typeof(MapConverter))]
[CborOptions(IsDefinite = true)]
public partial record MultiAssetMint(
    Dictionary<CborBytes, TokenBundleMint> Value
) : MultiAsset;