using Chrysalis.Cbor.Serialization.Attributes;
using Chrysalis.Cbor.Types.Cardano.Core.Protocol;
using Chrysalis.Cbor.Types.Cardano.Core.Header;
using Chrysalis.Cbor.Types.Cardano.Core.Transaction;

namespace Chrysalis.Cbor.Types.Cardano.Core.Governance;

/// <summary>
/// Abstract base for all Cardano governance actions defined in the Conway era.
/// </summary>
[CborSerializable]
[CborUnion]
public abstract partial record GovAction : CborBase { }

/// <summary>
/// Governance action to change protocol parameters.
/// </summary>
/// <param name="ActionType">The governance action type tag.</param>
/// <param name="GovActionId">The optional previous governance action identifier.</param>
/// <param name="ProtocolParamUpdate">The proposed protocol parameter changes.</param>
/// <param name="PolicyHash">The optional policy hash authorizing the change.</param>
[CborSerializable]
[CborList]
[CborIndex(0)]
public partial record ParameterChangeAction(
    [CborOrder(0)] int ActionType,
    [CborOrder(1)] GovActionId? GovActionId,
    [CborOrder(2)] ProtocolParamUpdate ProtocolParamUpdate,
    [CborOrder(3)] ReadOnlyMemory<byte>? PolicyHash
) : GovAction;

/// <summary>
/// Governance action to initiate a hard fork to a new protocol version.
/// </summary>
/// <param name="ActionType">The governance action type tag.</param>
/// <param name="GovActionId">The optional previous governance action identifier.</param>
/// <param name="ProtocolVersion">The target protocol version for the hard fork.</param>
[CborSerializable]
[CborList]
[CborIndex(1)]
public partial record HardForkInitiationAction(
     [CborOrder(0)] int ActionType,
     [CborOrder(1)] GovActionId? GovActionId,
     [CborOrder(2)] ProtocolVersion ProtocolVersion
 ) : GovAction;

/// <summary>
/// Governance action to withdraw funds from the treasury.
/// </summary>
/// <param name="ActionType">The governance action type tag.</param>
/// <param name="Withdrawals">The proposed treasury withdrawals mapped by reward account.</param>
/// <param name="PolicyHash">The optional policy hash authorizing the withdrawal.</param>
[CborSerializable]
[CborList]
[CborIndex(2)]
public partial record TreasuryWithdrawalsAction(
     [CborOrder(0)] int ActionType,
     [CborOrder(1)] Withdrawals Withdrawals,
     [CborOrder(2)] ReadOnlyMemory<byte>? PolicyHash
 ) : GovAction;

/// <summary>
/// Governance action expressing no confidence in the current constitutional committee.
/// </summary>
/// <param name="ActionType">The governance action type tag.</param>
/// <param name="GovActionId">The optional previous governance action identifier.</param>
[CborSerializable]
[CborList]
[CborIndex(3)]
public partial record NoConfidence(
    [CborOrder(0)] int ActionType,
    [CborOrder(1)] GovActionId? GovActionId
) : GovAction;

/// <summary>
/// Governance action to update the constitutional committee membership and quorum.
/// </summary>
/// <param name="ActionType">The governance action type tag.</param>
/// <param name="GovActionId">The optional previous governance action identifier.</param>
/// <param name="NewMembers">The list of credentials for members to remove.</param>
/// <param name="MemberTermLimits">The new committee members with their term limits.</param>
/// <param name="QuorumThreshold">The new quorum threshold as a rational number.</param>
[CborSerializable]
[CborList]
[CborIndex(4)]
public partial record UpdateCommittee(
    [CborOrder(0)] int ActionType,
    [CborOrder(1)] GovActionId? GovActionId,
    [CborOrder(2)] CborMaybeIndefList<Credential> NewMembers,
    [CborOrder(3)] MemberTermLimits MemberTermLimits,
    [CborOrder(4)] CborRationalNumber QuorumThreshold
) : GovAction;

/// <summary>
/// Governance action to adopt a new constitution.
/// </summary>
/// <param name="ActionType">The governance action type tag.</param>
/// <param name="GovActionId">The optional previous governance action identifier.</param>
/// <param name="Constitution">The new constitution to adopt.</param>
[CborSerializable]
[CborList]
[CborIndex(5)]
public partial record NewConstitution(
    [CborOrder(0)] int ActionType,
    [CborOrder(1)] GovActionId? GovActionId,
    [CborOrder(2)] Constitution Constitution
) : GovAction;

/// <summary>
/// A governance action that carries informational content only and has no on-chain effect.
/// </summary>
/// <param name="Value">The action type tag value.</param>
[CborSerializable]
[CborList]
[CborIndex(6)]
public partial record InfoAction(
    [CborOrder(0)] int Value
) : GovAction;
