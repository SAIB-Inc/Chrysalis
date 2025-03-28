using Chrysalis.Cbor.Serialization.Attributes;
using Chrysalis.Cbor.Serialization;
using Chrysalis.Cbor.Types.Cardano.Core.Common;
using Chrysalis.Cbor.Types.Cardano.Core.Protocol;

namespace Chrysalis.Cbor.Types.Cardano.Core.TransactionWitness;

[CborSerializable]
[CborUnion]
public abstract partial record Redeemers : CborBase { }

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
) : CborBase, ICborPreserveRaw;

[CborSerializable]
[CborList]
public partial record RedeemerKey(
    [CborOrder(0)] int Tag,
    [CborOrder(1)] ulong Index
) : CborBase, ICborPreserveRaw;

[CborSerializable]
[CborList]
public partial record RedeemerValue(
    [CborOrder(0)] PlutusData Data,
    [CborOrder(1)] ExUnits ExUnits
) : CborBase, ICborPreserveRaw;
