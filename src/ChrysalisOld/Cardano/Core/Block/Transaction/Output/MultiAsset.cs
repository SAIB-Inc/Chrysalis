using Chrysalis.Cardano.Cbor;
using Chrysalis.Cbor;

namespace Chrysalis.Cardano.Core;

[CborSerializable(CborType.Union)]
[CborUnionTypes([
    typeof(MultiAssetOutput),
    typeof(MultiAssetMint),
])]
public interface MultiAsset : ICbor;

public record MultiAssetOutput(Dictionary<CborBytes, TokenBundleOutput> Value) :
    CborMap<CborBytes, TokenBundleOutput>(Value), MultiAsset;

public record MultiAssetMint(Dictionary<CborBytes, TokenBundleMint> Value) :
    CborMap<CborBytes, TokenBundleMint>(Value), MultiAsset;