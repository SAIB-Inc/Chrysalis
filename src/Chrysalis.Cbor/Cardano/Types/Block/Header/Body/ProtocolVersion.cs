using Chrysalis.Cbor.Attributes;
using Chrysalis.Cbor.Serialization.Converters.Custom;
using Chrysalis.Cbor.Types;
using Chrysalis.Cbor.Types.Primitives;

namespace Chrysalis.Cbor.Cardano.Types.Block.Header.Body;

[CborConverter(typeof(CustomListConverter))]
public record ProtocolVersion(
    [CborIndex(0)] CborInt MajorProtocolVersion,
    [CborIndex(1)] CborUlong SequenceNumber
) : CborBase;
