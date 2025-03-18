using Chrysalis.Cbor.Attributes;
using Chrysalis.Cbor.Cardano.Types.Block.Transaction.Protocol;
using Chrysalis.Cbor.Cardano.Types.Block.Transaction.Script;
using Chrysalis.Cbor.Types;
using Chrysalis.Cbor.Serialization.Attributes;

namespace Chrysalis.Cbor.Cardano.Types.Block.Transaction.WitnessSet;

[CborSerializable]
[CborUnion]
public abstract partial record Redeemers : CborBase<Redeemers>
{
    [CborSerializable]
    public partial record RedeemerList(List<RedeemerEntry> Value) : Redeemers;

    [CborSerializable]
    public partial record RedeemerMap(Dictionary<RedeemerKey, RedeemerValue> Value) : Redeemers;
}

[CborSerializable]
[CborList]
public partial record RedeemerEntry(
    [CborIndex(0)] int Tag,
    [CborIndex(1)] ulong Index,
    [CborIndex(2)] PlutusData Data,
    [CborIndex(3)] ExUnits ExUnits
) : CborBase<RedeemerEntry>;

[CborSerializable]
[CborList]
public partial record RedeemerKey(
    [CborIndex(0)] int Tag,
    [CborIndex(1)] ulong Index
) : CborBase<RedeemerKey>;

[CborSerializable]
[CborList]
public partial record RedeemerValue(
    [CborIndex(0)] PlutusData Data,
    [CborIndex(1)] ExUnits ExUnits
) : CborBase<RedeemerValue>;
