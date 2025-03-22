using Chrysalis.Cbor.Cardano.Types.Block.Transaction.Protocol;
using Chrysalis.Cbor.Cardano.Types.Block.Transaction.Script;
using Chrysalis.Cbor.Types;
using Chrysalis.Cbor.Serialization.Attributes;
using Chrysalis.Cbor.Serialization;

namespace Chrysalis.Cbor.Cardano.Types.Block.Transaction.WitnessSet;

[CborSerializable]
[CborUnion]
public abstract partial record Redeemers : CborBase<Redeemers>
{
}

[CborSerializable]
public partial record RedeemerList(List<RedeemerEntry> Value) : Redeemers, ICborPreserveRaw;

[CborSerializable]
public partial record RedeemerMap(Dictionary<RedeemerKey, RedeemerValue> Value) : Redeemers, ICborPreserveRaw;

[CborSerializable]
[CborList]
public partial record RedeemerEntry(
    [CborOrder(0)] int Tag,
    [CborOrder(1)] ulong Index,
    [CborOrder(2)] PlutusData Data,
    [CborOrder(3)] ExUnits ExUnits
) : CborBase<RedeemerEntry>, ICborPreserveRaw;

[CborSerializable]
[CborList]
public partial record RedeemerKey(
    [CborOrder(0)] int Tag,
    [CborOrder(1)] ulong Index
) : CborBase<RedeemerKey>, ICborPreserveRaw;

[CborSerializable]
[CborList]
public partial record RedeemerValue(
    [CborOrder(0)] PlutusData Data,
    [CborOrder(1)] ExUnits ExUnits
) : CborBase<RedeemerValue>, ICborPreserveRaw;
