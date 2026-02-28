using Chrysalis.Cbor.Serialization.Attributes;

namespace Chrysalis.Cbor.Types.Cardano.Core.Header;

/// <summary>
/// The protocol version consisting of a major version and a sequence number.
/// </summary>
/// <param name="MajorProtocolVersion">The major protocol version number.</param>
/// <param name="SequenceNumber">The minor version or sequence number.</param>
[CborSerializable]
[CborList]
public partial record ProtocolVersion(
    [CborOrder(0)] int MajorProtocolVersion,
    [CborOrder(1)] ulong SequenceNumber
) : CborBase;
