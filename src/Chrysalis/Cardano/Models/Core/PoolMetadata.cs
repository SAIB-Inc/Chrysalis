using Chrysalis.Cardano.Models.Cbor;
using Chrysalis.Cbor;

namespace Chrysalis.Cardano.Models.Core;

[CborSerializable(CborType.List)]
public record PoolMetadata(
    [CborProperty(0)] CborBytes Url, 
    [CborProperty(1)] CborBytes PoolMetadataHash
) : ICbor;