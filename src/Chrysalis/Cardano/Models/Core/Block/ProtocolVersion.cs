using Chrysalis.Cardano.Models.Cbor;
using Chrysalis.Cbor;

namespace Chrysalis.Cardano.Models.Core.Block;

[CborSerializable(CborType.List)]
public record ProtocolVersion(
    [CborProperty(0)] CborInt MajorProtocolVersion,
    [CborProperty(1)] CborUlong SequenceNumber
) : ICbor;
