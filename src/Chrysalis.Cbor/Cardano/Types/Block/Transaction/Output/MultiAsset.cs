using Chrysalis.Cbor.Serialization.Attributes;
using Chrysalis.Cbor.Types;
using static Chrysalis.Cbor.Cardano.Types.Block.Transaction.Output.TokenBundle;

namespace Chrysalis.Cbor.Cardano.Types.Block.Transaction.Output;

// [CborSerializable]
[CborUnion]
public abstract partial record MultiAsset : CborBase<MultiAsset>
{
    // [CborSerializable]
    public partial record MultiAssetOutput(
        Dictionary<byte[], TokenBundleOutput> Value
    ) : MultiAsset;


    // [CborSerializable]
    public partial record MultiAssetMint(
        Dictionary<byte[], TokenBundleMint> Value
    ) : MultiAsset;
}