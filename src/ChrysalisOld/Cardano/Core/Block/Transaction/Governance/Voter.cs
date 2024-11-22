using Chrysalis.Cardano.Cbor;
using Chrysalis.Cbor;

namespace Chrysalis.Cardano.Core;

[CborSerializable(CborType.List)]
public record Voter( 
    [CborProperty(0)] CborInt Tag,
    [CborProperty(1)] CborBytes Hash
) : RawCbor;