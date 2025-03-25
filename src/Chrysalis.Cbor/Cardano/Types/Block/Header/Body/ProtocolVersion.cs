using Chrysalis.Cbor.Serialization.Attributes;
using Chrysalis.Cbor.Types;

namespace Chrysalis.Cbor.Cardano.Types.Block.Header.Body;

[CborSerializable]
[CborList]
public partial record ProtocolVersion(
    [CborOrder(0)] int MajorProtocolVersion,
    [CborOrder(1)] ulong SequenceNumber
) : CborBase;
