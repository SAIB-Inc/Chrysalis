using Chrysalis.Cardano.Cbor;
using Chrysalis.Cbor;

namespace Chrysalis.Cardano.Core;

[CborSerializable(CborType.List)]
public record VotingProcedure( 
    [CborProperty(0)] CborInt Vote,
    [CborProperty(1)] CborNullable<Anchor> Anchor
) : RawCbor;