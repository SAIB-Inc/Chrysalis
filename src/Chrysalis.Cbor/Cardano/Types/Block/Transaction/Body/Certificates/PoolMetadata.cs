using Chrysalis.Cbor.Attributes;
using Chrysalis.Cbor.Serialization.Converters.Custom;
using Chrysalis.Cbor.Types;
using Chrysalis.Cbor.Types.Primitives;

namespace Chrysalis.Cbor.Cardano.Types.Block.Transaction.Body.Certificates;

[CborConverter(typeof(CustomListConverter))]
public record PoolMetadata(
    [CborIndex(0)] CborText Url,
    [CborIndex(1)] CborBytes PoolMetadataHash
) : CborBase;