using Chrysalis.Cardano.Cbor;
using Chrysalis.Cbor;

namespace Chrysalis.Cardano.Core;

[CborSerializable(CborType.List)]
public record Anchor(
    [CborProperty(0)] CborText AnchorUrl, 
    [CborProperty(1)] CborBytes AnchorDataHash
) : RawCbor;