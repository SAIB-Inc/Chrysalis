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
public abstract partial record GovAction : CborBase
{
}

[CborSerializable]
[CborList]
public partial record ParameterChangeAction(
    [CborOrder(0)] int ActionType,
    [CborOrder(1)] GovActionId? GovActionId,
    [CborOrder(2)] ProtocolParamUpdate ProtocolParamUpdate,
    [CborOrder(3)] byte[]? PolicyHash
) : GovAction;


[CborSerializable]
[CborList]
public partial record HardForkInitiationAction(
     [CborOrder(0)] int ActionType,
     [CborOrder(1)] GovActionId? GovActionId,
     [CborOrder(2)] ProtocolVersion ProtocolVersion
 ) : GovAction;


[CborSerializable]
[CborList]
public partial record TreasuryWithdrawalsAction(
     [CborOrder(0)] int ActionType,
     [CborOrder(1)] Withdrawals Withdrawals,
     [CborOrder(2)] byte[]? PolicyHash
 ) : GovAction;


[CborSerializable]
[CborList]
public partial record NoConfidence(
    [CborOrder(0)] int ActionType,
    [CborOrder(1)] GovActionId? GovActionId
) : GovAction;

[CborSerializable]
[CborList]
public partial record UpdateCommittee(
    [CborOrder(0)] int ActionType,
    [CborOrder(1)] GovActionId? GovActionId,
    [CborOrder(2)] CborMaybeIndefList<Credential> NewMembers,
    [CborOrder(3)] MemberTermLimits MemberTermLimits,
    [CborOrder(4)] CborRationalNumber QuorumThreshold
) : GovAction;

[CborSerializable]
[CborList]
public partial record NewConstitution(
    [CborOrder(0)] int ActionType,
    [CborOrder(1)] GovActionId? GovActionId,
    [CborOrder(2)] Constitution Constitution
) : GovAction;

[CborSerializable]
[CborList]
public partial record InfoAction(
    [CborOrder(0)] int Value
) : GovAction;
