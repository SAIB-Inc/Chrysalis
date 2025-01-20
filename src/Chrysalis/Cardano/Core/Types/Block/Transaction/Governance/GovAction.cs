using Chrysalis.Cardano.Core.Types.Block.Header.Body;
using Chrysalis.Cardano.Core.Types.Block.Transaction.Body;
using Chrysalis.Cardano.Core.Types.Block.Transaction.Protocol;
using Chrysalis.Cbor.Attributes;
using Chrysalis.Cbor.Converters.Primitives;
using Chrysalis.Cbor.Types;
using Chrysalis.Cbor.Types.Collections;
using Chrysalis.Cbor.Types.Primitives;

namespace Chrysalis.Cardano.Core.Types.Block.Transaction.Governance;

[CborConverter(typeof(UnionConverter))]
public abstract record GovAction : CborBase;


[CborConverter(typeof(CustomListConverter))]
public record ParameterChangeAction(
    [CborProperty(0)] CborInt ActionType,
    [CborProperty(1)] CborNullable<GovActionId> GovActionId,
    [CborProperty(2)] ProtocolParamUpdate ProtocolParamUpdate,
    [CborProperty(3)] CborNullable<CborBytes> PolicyHash
) : GovAction;


[CborConverter(typeof(CustomListConverter))]
public record HardForkInitiationAction(
    [CborProperty(0)] CborInt ActionType,
    [CborProperty(1)] CborNullable<GovActionId> GovActionId,
    [CborProperty(2)] ProtocolVersion ProtocolVersion
) : GovAction;


[CborConverter(typeof(CustomListConverter))]
public record TreasuryWithdrawalsAction(
    [CborProperty(0)] CborInt ActionType,
    [CborProperty(1)] Withdrawals Withdrawals,
    [CborProperty(2)] CborNullable<CborBytes> PolicyHash
) : GovAction;


[CborConverter(typeof(CustomListConverter))]
public record NoConfidence(
    [CborProperty(0)] CborInt ActionType,
    [CborProperty(1)] CborNullable<GovActionId> GovActionId
) : GovAction;

[CborConverter(typeof(CustomListConverter))]
public record UpdateCommittee(
    [CborProperty(0)] CborInt ActionType,
    [CborProperty(1)] CborNullable<GovActionId> GovActionId,
    [CborProperty(2)] CborMaybeIndefList<Credential> NewMembers,
    [CborProperty(3)] MemberTermLimits MemberTermLimits,
    [CborProperty(4)] CborRationalNumber QuorumThreshold
) : GovAction;

[CborConverter(typeof(CustomListConverter))]
public record NewConstitution(
    [CborProperty(0)] CborInt ActionType,
    [CborProperty(1)] CborNullable<GovActionId> GovActionId,
    [CborProperty(2)] Constitution Constitution
) : GovAction;

[CborConverter(typeof(CustomListConverter))]
public record InfoAction(
    [CborProperty(0)] CborInt Value
) : GovAction;