using Chrysalis.Cardano.Models.Cbor;
using Chrysalis.Cbor;

namespace Chrysalis.Cardano.Models.Core.Governance;

public record VotingProcedures(Dictionary<Voter, VoterChoices> Value)
    : CborMap<Voter, VoterChoices>(Value), ICbor;

public record VoterChoices(Dictionary<GovActionId, VotingProcedure> Value)
    : CborMap<GovActionId, VotingProcedure>(Value), ICbor;