using Chrysalis.Cbor.Attributes;
using Chrysalis.Cbor.Converters.Primitives;
using Chrysalis.Cbor.Types;
using Chrysalis.Cbor.Types.Collections;

namespace Chrysalis.Cardano.Core.Types.Block.Transaction.Governance;

[CborConverter(typeof(MapConverter))]
public record VotingProcedures(
    Dictionary<Voter, CborMap<GovActionId, VotingProcedure>> Value
) : CborBase;


[CborConverter(typeof(MapConverter))]
public record VoterChoices(
    Dictionary<GovActionId, VotingProcedure> Value
) : CborBase;