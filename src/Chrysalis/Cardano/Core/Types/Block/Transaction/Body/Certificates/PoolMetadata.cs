using Chrysalis.Cbor.Attributes;
using Chrysalis.Cbor.Converters.Primitives;
using Chrysalis.Cbor.Types;
using Chrysalis.Cbor.Types.Primitives;

namespace Chrysalis.Cardano.Core.Types.Block.Transaction.Body.Certificates;

[CborConverter(typeof(CustomListConverter))]
public record PoolMetadata(
    [CborProperty(0)] CborText Url,
    [CborProperty(1)] CborBytes PoolMetadataHash
) : CborBase;