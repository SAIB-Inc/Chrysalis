using Chrysalis.Cbor.Serialization.Attributes;

namespace Chrysalis.Cbor.Types.Cardano.Core.Common;

[CborSerializable]
[CborUnion]
public abstract partial record Value : CborBase
{
}

[CborSerializable]
public partial record Lovelace(ulong Value) : Value;

[CborSerializable]
[CborList]
public partial record LovelaceWithMultiAsset(
     [CborOrder(0)] Lovelace LovelaceValue,
     [CborOrder(1)] MultiAssetOutput MultiAsset
 ) : Value;
