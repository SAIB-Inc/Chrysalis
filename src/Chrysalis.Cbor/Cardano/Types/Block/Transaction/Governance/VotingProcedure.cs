using Chrysalis.Cbor.Attributes;
using Chrysalis.Cbor.Serialization.Converters.Custom;
using Chrysalis.Cbor.Types;
using Chrysalis.Cbor.Types.Primitives;

namespace Chrysalis.Cbor.Cardano.Types.Block.Transaction.Governance;

[CborConverter(typeof(CustomListConverter))]
public record VotingProcedure(
    [CborIndex(0)] CborInt Vote,
    [CborIndex(1)] CborNullable<Anchor> Anchor
) : CborBase;