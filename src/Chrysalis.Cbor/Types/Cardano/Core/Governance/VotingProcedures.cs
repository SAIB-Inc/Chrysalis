using Chrysalis.Cbor.Serialization.Attributes;

namespace Chrysalis.Cbor.Types.Cardano.Core.Governance;

/// <summary>
/// Maps voters to their governance action votes within a transaction.
/// </summary>
/// <param name="Value">Dictionary mapping each voter to their governance action voting procedures.</param>
[CborSerializable]
public partial record VotingProcedures(Dictionary<Voter, GovActionIdVotingProcedure> Value) : CborBase;

/// <summary>
/// Maps governance action identifiers to their voting procedures for a specific voter.
/// </summary>
/// <param name="Value">Dictionary mapping governance action IDs to voting procedures.</param>
[CborSerializable]
public partial record GovActionIdVotingProcedure(Dictionary<GovActionId, VotingProcedure> Value) : CborBase;

/// <summary>
/// Maps governance action identifiers to voting procedures, representing a voter's choices.
/// </summary>
/// <param name="Value">Dictionary mapping governance action IDs to voting procedures.</param>
[CborSerializable]
public partial record VoterChoices(Dictionary<GovActionId, VotingProcedure> Value) : CborBase;
