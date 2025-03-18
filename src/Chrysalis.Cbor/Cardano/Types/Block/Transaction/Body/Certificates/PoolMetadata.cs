using Chrysalis.Cbor.Attributes;
using Chrysalis.Cbor.Serialization.Attributes;

using Chrysalis.Cbor.Types;

namespace Chrysalis.Cbor.Cardano.Types.Block.Transaction.Body.Certificates;

// [CborSerializable]
[CborList]
public partial record PoolMetadata(
    [CborIndex(0)] string Url,
    [CborIndex(1)] byte[] PoolMetadataHash
) : CborBase<PoolMetadata>;