using Chrysalis.Cbor.Attributes;
using Chrysalis.Cbor.Serialization.Attributes;

using Chrysalis.Cbor.Types;

namespace Chrysalis.Cbor.Cardano.Types.Block.Header.Body;

// [CborSerializable]
[CborList]
public partial record ProtocolVersion(
    [CborIndex(0)] int MajorProtocolVersion,
    [CborIndex(1)] ulong SequenceNumber
) : CborBase<ProtocolVersion>;
