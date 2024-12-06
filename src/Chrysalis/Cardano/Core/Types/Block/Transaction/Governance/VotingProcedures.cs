using Chrysalis.Cbor.Attributes;
using Chrysalis.Cbor.Converters.Primitives;
using Chrysalis.Cbor.Types;

namespace Chrysalis.Cardano.Core.Types.Block.Transaction.Governance;

[CborConverter(typeof(MapConverter))]
public record VotingProcedures(
    Dictionary<GovActionId, VotingProcedure> Value
) : CborBase;


[CborConverter(typeof(MapConverter))]
public record VoterChoices(
    Dictionary<GovActionId, VotingProcedure> Value
) : CborBase;