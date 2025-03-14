using Chrysalis.Cbor.Attributes;
using Chrysalis.Cbor.Serialization.Attributes;


using Chrysalis.Cbor.Types;
using static Chrysalis.Cbor.Cardano.Types.Block.Transaction.Output.MultiAsset;

namespace Chrysalis.Cbor.Cardano.Types.Block.Transaction.Output;

// [CborSerializable]
[CborUnion]
public abstract partial record Value : CborBase<Value>
{
    // [CborSerializable]
    public partial record Lovelace(ulong Value) : Value;

    // [CborSerializable]
    [CborList]
    public partial record LovelaceWithMultiAsset(
         [CborIndex(0)] Lovelace LovelaceValue,
         [CborIndex(1)] MultiAssetOutput MultiAsset
     ) : Value;
}
