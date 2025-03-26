using Chrysalis.Cbor.Serialization.Attributes;
using Chrysalis.Cbor.Types;

namespace Chrysalis.Cbor.Types.Cardano.Core.Certificates;

[CborSerializable]
[CborList]
public partial record PoolMetadata(
    [CborOrder(0)] string Url,
    [CborOrder(1)] byte[] PoolMetadataHash
) : CborBase;