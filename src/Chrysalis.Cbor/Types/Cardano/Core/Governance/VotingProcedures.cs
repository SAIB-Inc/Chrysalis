using Chrysalis.Cbor.Serialization.Attributes;

namespace Chrysalis.Cbor.Types.Cardano.Core.Governance;

[CborSerializable]
public partial record VotingProcedures(Dictionary<Voter, GovActionIdVotingProcedure> Value) : CborBase;

[CborSerializable]
public partial record GovActionIdVotingProcedure(Dictionary<GovActionId, VotingProcedure> Value) : CborBase;

[CborSerializable]
public partial record VoterChoices(Dictionary<GovActionId, VotingProcedure> Value) : CborBase;