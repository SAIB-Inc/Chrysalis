using Chrysalis.Cbor.Serialization.Attributes;

namespace Chrysalis.Cbor.Types.Cardano.Core.Common;

[CborSerializable]
[CborUnion]
public abstract partial record MultiAsset : CborBase { }

[CborSerializable]
public partial record MultiAssetOutput(
    Dictionary<byte[], TokenBundleOutput> Value
) : MultiAsset;


[CborSerializable]
public partial record MultiAssetMint(
    Dictionary<byte[], TokenBundleMint> Value
) : MultiAsset;