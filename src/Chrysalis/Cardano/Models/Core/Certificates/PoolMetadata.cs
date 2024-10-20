using Chrysalis.Cardano.Models.Cbor;
using Chrysalis.Cbor;

namespace Chrysalis.Cardano.Models.Core.Certificates;

[CborSerializable(CborType.List)]
public record PoolMetadata(
    [CborProperty(0)] CborText Url, 
    [CborProperty(1)] CborBytes PoolMetadataHash
) : RawCbor;