using Chrysalis.Cbor.Attributes;
using Chrysalis.Cbor.Serialization.Converters.Custom;
using Chrysalis.Cbor.Cardano.Types.Block.Header.Body;
using Chrysalis.Cbor.Cardano.Types.Block.Transaction.Body;
using Chrysalis.Cbor.Cardano.Types.Block.Transaction.Protocol;
using Chrysalis.Cbor.Types.Custom;
using Chrysalis.Cbor.Types.Primitives;
using Chrysalis.Cbor.Types;

namespace Chrysalis.Cbor.Cardano.Types.Block.Transaction.Governance;

[CborConverter(typeof(UnionConverter))]
public abstract record GovAction : CborBase;


[CborConverter(typeof(CustomListConverter))]
public record ParameterChangeAction(
    [CborIndex(0)] CborInt ActionType,
    [CborIndex(1)] CborNullable<GovActionId> GovActionId,
    [CborIndex(2)] ProtocolParamUpdate ProtocolParamUpdate,
    [CborIndex(3)] CborNullable<CborBytes> PolicyHash
) : GovAction;


[CborConverter(typeof(CustomListConverter))]
public record HardForkInitiationAction(
    [CborIndex(0)] CborInt ActionType,
    [CborIndex(1)] CborNullable<GovActionId> GovActionId,
    [CborIndex(2)] ProtocolVersion ProtocolVersion
) : GovAction;


[CborConverter(typeof(CustomListConverter))]
public record TreasuryWithdrawalsAction(
    [CborIndex(0)] CborInt ActionType,
    [CborIndex(1)] Withdrawals Withdrawals,
    [CborIndex(2)] CborNullable<CborBytes> PolicyHash
) : GovAction;


[CborConverter(typeof(CustomListConverter))]
public record NoConfidence(
    [CborIndex(0)] CborInt ActionType,
    [CborIndex(1)] CborNullable<GovActionId> GovActionId
) : GovAction;

[CborConverter(typeof(CustomListConverter))]
public record UpdateCommittee(
    [CborIndex(0)] CborInt ActionType,
    [CborIndex(1)] CborNullable<GovActionId> GovActionId,
    [CborIndex(2)] CborMaybeIndefList<Credential> NewMembers,
    [CborIndex(3)] MemberTermLimits MemberTermLimits,
    [CborIndex(4)] CborRationalNumber QuorumThreshold
) : GovAction;

[CborConverter(typeof(CustomListConverter))]
public record NewConstitution(
    [CborIndex(0)] CborInt ActionType,
    [CborIndex(1)] CborNullable<GovActionId> GovActionId,
    [CborIndex(2)] Constitution Constitution
) : GovAction;

[CborConverter(typeof(CustomListConverter))]
public record InfoAction(
    [CborIndex(0)] CborInt Value
) : GovAction;