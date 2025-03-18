using Chrysalis.Cbor.Serialization.Attributes;
using Chrysalis.Cbor.Types;

namespace Chrysalis.Cbor.Cardano.Types.Block.Transaction.Output;

[CborSerializable]
[CborUnion]
public abstract partial record MultiAsset : CborBase<MultiAsset>
{
    [CborSerializable]
    public partial record MultiAssetOutput(
        Dictionary<byte[], TokenBundle.TokenBundleOutput> Value
    ) : MultiAsset;


    [CborSerializable]
    public partial record MultiAssetMint(
        Dictionary<byte[], TokenBundle.TokenBundleMint> Value
    ) : MultiAsset;
}