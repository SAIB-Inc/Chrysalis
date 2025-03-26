using Chrysalis.Cbor.Serialization.Attributes;

namespace Chrysalis.Cbor.Types.Cardano.Core.Header;

[CborSerializable]
[CborList]
public partial record ProtocolVersion(
    [CborOrder(0)] int MajorProtocolVersion,
    [CborOrder(1)] ulong SequenceNumber
) : CborBase;
