using Chrysalis.Cbor.Attributes;
using Chrysalis.Cbor.Converters.Primitives;
using Chrysalis.Cbor.Types;
using Chrysalis.Cbor.Types.Primitives;

namespace Chrysalis.Cardano.Core.Types.Block.Header.Body;

[CborConverter(typeof(CustomListConverter))]
public record ProtocolVersion(
    [CborProperty(0)] CborInt MajorProtocolVersion,
    [CborProperty(1)] CborUlong SequenceNumber
) : CborBase;
