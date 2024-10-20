using Chrysalis.Cardano.Models.Cbor;
using Chrysalis.Cbor;

namespace Chrysalis.Cardano.Models.Core.Governance;

[CborSerializable(CborType.List)]
public record VotingProcedure( 
    [CborProperty(0)] CborInt Vote,
    [CborProperty(1)] CborNullable<Anchor> Anchor
) : RawCbor;