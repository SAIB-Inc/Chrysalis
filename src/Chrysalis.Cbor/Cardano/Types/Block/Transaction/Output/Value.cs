using Chrysalis.Cbor.Serialization.Attributes;
using Chrysalis.Cbor.Types;

namespace Chrysalis.Cbor.Cardano.Types.Block.Transaction.Output;

[CborSerializable]
[CborUnion]
public abstract partial record Value : CborBase<Value>
{
    [CborSerializable]
    public partial record Lovelace(ulong Value) : Value;

    [CborSerializable]
    [CborList]
    public partial record LovelaceWithMultiAsset(
         [CborOrder(0)] Lovelace LovelaceValue,
         [CborOrder(1)] MultiAssetOutput MultiAsset
     ) : Value;
}
