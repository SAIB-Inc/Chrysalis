using Chrysalis.Codec.V2.Serialization;
using Chrysalis.Codec.V2.Serialization.Attributes;

namespace Chrysalis.Codec.V2.Types.Cardano.Core.Governance;

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
