using Chrysalis.Cbor.Serialization.Attributes;


using Chrysalis.Cbor.Types;

namespace Chrysalis.Cbor.Cardano.Types.Block.Transaction.Body.Certificates;

[CborSerializable]
[CborList]
public partial record MoveInstantaneousReward(
    [CborOrder(0)] int InstantaneousRewardSource,
    [CborOrder(1)] Target InstantaneousRewardTarget
) : CborBase<MoveInstantaneousReward>;


[CborSerializable]
[CborUnion]
public abstract partial record Target : CborBase<Target>
{
    [CborSerializable]
    public partial record StakeCredentials(Dictionary<Credential, ulong> Value) : Target;


    [CborSerializable]
    public partial record OtherAccountingPot(ulong Value) : Target;
}

