using Chrysalis.Cardano.Models.Cbor;
using Chrysalis.Cbor;

namespace Chrysalis.Cardano.Models.Core.Block.Transaction.Governance;

[CborSerializable(CborType.List)]
public record Voter( 
    [CborProperty(0)] CborInt Tag,
    [CborProperty(1)] CborBytes Hash
) : RawCbor;