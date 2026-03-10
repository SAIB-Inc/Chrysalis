using Chrysalis.Codec.Serialization;
using Chrysalis.Codec.Serialization.Attributes;

namespace Chrysalis.Codec.Types.Cardano.Core.Governance;

[CborSerializable]
public partial record VotingProcedures(Dictionary<Voter, GovActionIdVotingProcedure> Value) : ICborType
{
    public ReadOnlyMemory<byte> Raw { get; set; }
    public int ConstrIndex { get; set; }
    public bool IsIndefinite { get; set; }
}

[CborSerializable]
public partial record GovActionIdVotingProcedure(Dictionary<GovActionId, VotingProcedure> Value) : ICborType
{
    public ReadOnlyMemory<byte> Raw { get; set; }
    public int ConstrIndex { get; set; }
    public bool IsIndefinite { get; set; }
}

[CborSerializable]
public partial record VoterChoices(Dictionary<GovActionId, VotingProcedure> Value) : ICborType
{
    public ReadOnlyMemory<byte> Raw { get; set; }
    public int ConstrIndex { get; set; }
    public bool IsIndefinite { get; set; }
}
