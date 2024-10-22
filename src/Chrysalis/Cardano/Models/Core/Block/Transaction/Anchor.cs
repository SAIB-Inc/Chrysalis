using Chrysalis.Cardano.Models.Cbor;
using Chrysalis.Cbor;

namespace Chrysalis.Cardano.Models.Core.Block.Transaction;

[CborSerializable(CborType.List)]
public record Anchor(
    [CborProperty(0)] CborText AnchorUrl, 
    [CborProperty(1)] CborBytes AnchorDataHash
) : RawCbor;