using Chrysalis.Cardano.Cbor;
using Chrysalis.Cbor;

namespace Chrysalis.Cardano.Core;

[CborSerializable(CborType.List)]
public record PoolMetadata(
    [CborProperty(0)] CborText Url, 
    [CborProperty(1)] CborBytes PoolMetadataHash
) : RawCbor;