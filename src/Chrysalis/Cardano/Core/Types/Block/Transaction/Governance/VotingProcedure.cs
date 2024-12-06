using Chrysalis.Cbor.Attributes;
using Chrysalis.Cbor.Converters.Primitives;
using Chrysalis.Cbor.Types;
using Chrysalis.Cbor.Types.Primitives;

namespace Chrysalis.Cardano.Core.Types.Block.Transaction.Governance;

[CborConverter(typeof(CustomListConverter))]
public record VotingProcedure(
    [CborProperty(0)] CborInt Vote,
    [CborProperty(1)] CborNullable<Anchor> Anchor
) : CborBase;