using Chrysalis.Cbor.Attributes;

using Chrysalis.Cbor.Cardano.Types.Block.Header.Body;
using Chrysalis.Cbor.Cardano.Types.Block.Transaction.Body;
using Chrysalis.Cbor.Cardano.Types.Block.Transaction.Protocol;
using Chrysalis.Cbor.Types.Custom;
using Chrysalis.Cbor.Types.Primitives;
using Chrysalis.Cbor.Types;
using Chrysalis.Cbor.Serialization.Attributes;

namespace Chrysalis.Cbor.Cardano.Types.Block.Transaction.Governance;

[CborSerializable]
[CborUnion]
public abstract partial record GovAction : CborBase<GovAction>
{
    [CborSerializable]
    [CborList]
    public partial record ParameterChangeAction(
        [CborIndex(0)] int ActionType,
        [CborIndex(1)] GovActionId? GovActionId,
        [CborIndex(2)] ProtocolParamUpdate ProtocolParamUpdate,
        [CborIndex(3)] byte[]? PolicyHash
    ) : GovAction;


    [CborSerializable]
    [CborList]
    public partial record HardForkInitiationAction(
         [CborIndex(0)] int ActionType,
         [CborIndex(1)] GovActionId? GovActionId,
         [CborIndex(2)] ProtocolVersion ProtocolVersion
     ) : GovAction;


    [CborSerializable]
    [CborList]
    public partial record TreasuryWithdrawalsAction(
         [CborIndex(0)] int ActionType,
         [CborIndex(1)] Withdrawals Withdrawals,
         [CborIndex(2)] byte[]? PolicyHash
     ) : GovAction;


    [CborSerializable]
    [CborList]
    public partial record NoConfidence(
        [CborIndex(0)] int ActionType,
        [CborIndex(1)] GovActionId? GovActionId
    ) : GovAction;

    [CborSerializable]
    [CborList]
    public partial record UpdateCommittee(
        [CborIndex(0)] int ActionType,
        [CborIndex(1)] GovActionId? GovActionId,
        [CborIndex(2)] CborMaybeIndefList<Credential> NewMembers,
        [CborIndex(3)] MemberTermLimits MemberTermLimits,
        [CborIndex(4)] CborRationalNumber QuorumThreshold
    ) : GovAction;

    [CborSerializable]
    [CborList]
    public partial record NewConstitution(
        [CborIndex(0)] int ActionType,
        [CborIndex(1)] GovActionId? GovActionId,
        [CborIndex(2)] Constitution Constitution
    ) : GovAction;

    [CborSerializable]
    [CborList]
    public partial record InfoAction(
        [CborIndex(0)] int Value
    ) : GovAction;
}