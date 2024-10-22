using Chrysalis.Cbor;
using Chrysalis.Cardano.Models.Cbor;
using Chrysalis.Cardano.Models.Core.Block.Header;
using Chrysalis.Cardano.Models.Core.Block.Transaction.Body;
using Chrysalis.Cardano.Models.Core.Block.Transaction.Protocol;

namespace Chrysalis.Cardano.Models.Core.Block.Transaction.Governance;

[CborSerializable(CborType.Union)]
[CborUnionTypes([
    typeof(ParameterChangeAction),
    typeof(HardForkInitiationAction),
    typeof(TreasuryWithdrawalsAction),
    typeof(NoConfidence),
    typeof(UpdateCommittee),    
    typeof(NewConstitution),
    typeof(InfoAction),    
])]
public record GovAction : RawCbor;

[CborSerializable(CborType.List)]
public record ParameterChangeAction(
    [CborProperty(0)] CborInt ActionType,
    [CborProperty(1)] CborNullable<GovActionId> GovActionId,
    [CborProperty(2)] ProtocolParamUpdate ProtocolParamUpdate,
    [CborProperty(3)] CborNullable<CborBytes> PolicyHash
) : GovAction;

[CborSerializable(CborType.List)]
public record HardForkInitiationAction(
    [CborProperty(0)] CborInt ActionType,
    [CborProperty(1)] CborNullable<GovActionId> GovActionId,
    [CborProperty(2)] ProtocolVersion ProtocolVersion
) : GovAction;

[CborSerializable(CborType.List)]
public record TreasuryWithdrawalsAction(
    [CborProperty(0)] CborInt ActionType,
    [CborProperty(1)] Withdrawals Withdrawals,
    [CborProperty(2)] CborNullable<CborBytes> PolicyHash
) : GovAction;

[CborSerializable(CborType.List)]
public record NoConfidence(
    [CborProperty(0)] CborInt ActionType,
    [CborProperty(1)] CborNullable<GovActionId> GovActionId
) : GovAction;

[CborSerializable(CborType.List)]
public record UpdateCommittee(
    [CborProperty(0)] CborInt ActionType,
    [CborProperty(1)] CborNullable<GovActionId> GovActionId,
    [CborProperty(2)] CborDefiniteList<Credential> NewMembers,
    [CborProperty(3)] MemberTermLimits MemberTermLimits,
    [CborProperty(4)] CborRationalNumber QuorumThreshold
) : GovAction;

[CborSerializable(CborType.List)]
public record NewConstitution(
    [CborProperty(0)] CborInt ActionType,
    [CborProperty(1)] CborNullable<GovActionId> GovActionId,
    [CborProperty(2)] Constitution Constitution
) : GovAction;

[CborSerializable(CborType.List)]
public record InfoAction(
    [CborProperty(0)] CborInt Value
) : GovAction;