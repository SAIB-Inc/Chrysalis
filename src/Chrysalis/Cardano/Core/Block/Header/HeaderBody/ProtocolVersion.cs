using Chrysalis.Cardano.Cbor;
using Chrysalis.Cbor;

namespace Chrysalis.Cardano.Core;

[CborSerializable(CborType.List)]
public record ProtocolVersion(
    [CborProperty(0)] CborInt MajorProtocolVersion,
    [CborProperty(1)] CborUlong SequenceNumber
) : RawCbor;
