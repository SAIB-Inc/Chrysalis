using Chrysalis.Cbor.Serialization.Attributes;
using Chrysalis.Cbor.Types;

namespace Chrysalis.Cbor.Cardano.Types.Block.Transaction.Governance;

[CborSerializable]
public partial record VotingProcedures(
    Dictionary<Voter, GovActionIdVotingProcedure> Value
) : CborBase<VotingProcedure>;

[CborSerializable]
public partial record GovActionIdVotingProcedure(Dictionary<GovActionId, VotingProcedure> Value) : CborBase<GovActionIdVotingProcedure>;

[CborSerializable]
public partial record VoterChoices(
    Dictionary<GovActionId, VotingProcedure> Value
) : CborBase<VoterChoices>;