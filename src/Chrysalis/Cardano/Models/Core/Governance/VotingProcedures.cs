using Chrysalis.Cbor;

namespace Chrysalis.Cardano.Models.Core.Governance;

public record VotingProcedures<K, V>(Dictionary<K, V> Value)
    : CborMap<K, V>(Value), ICbor where K : Voter where V : VoterChoices<GovActionId, VotingProcedure>;

public record VoterChoices<K, V>(Dictionary<K, V> Value)
    : CborMap<K, V>(Value), ICbor where K : GovActionId where V : VotingProcedure;