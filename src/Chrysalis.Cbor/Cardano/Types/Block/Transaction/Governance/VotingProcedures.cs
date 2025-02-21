using Chrysalis.Cbor.Attributes;
using Chrysalis.Cbor.Serialization.Converters.Primitives;
using Chrysalis.Cbor.Types;
using Chrysalis.Cbor.Types.Primitives;

namespace Chrysalis.Cbor.Cardano.Types.Block.Transaction.Governance;

[CborConverter(typeof(MapConverter))]
public record VotingProcedures(
    Dictionary<Voter, CborMap<GovActionId, VotingProcedure>> Value
) : CborBase;


[CborConverter(typeof(MapConverter))]
public record VoterChoices(
    Dictionary<GovActionId, VotingProcedure> Value
) : CborBase;