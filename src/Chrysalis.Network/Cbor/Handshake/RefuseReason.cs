using Chrysalis.Cbor.Serialization.Attributes;
using Chrysalis.Cbor.Types;

namespace Chrysalis.Network.Cbor.Handshake;


[CborSerializable]
[CborUnion]
public partial record RefuseReason : CborBase;

[CborSerializable]
[CborList]
[CborIndex(0)]
public partial record VersionMismatch(
    [CborOrder(0)] int Idx,
    [CborOrder(1)] CborDefList<int> VersionNumbers
) : RefuseReason;

[CborSerializable]
[CborList]
[CborIndex(1)]
public partial record HandshakeDecodeError(
    [CborOrder(0)] int Idx,
    [CborOrder(1)] int VersionNumber,
    [CborOrder(2)] ReadOnlyMemory<byte> ErrorData
) : RefuseReason;

[CborSerializable]
[CborList]
[CborIndex(2)]
public partial record Refused(
    [CborOrder(0)] int Idx,
    [CborOrder(1)] int VersionNumber,
    [CborOrder(2)] ReadOnlyMemory<byte> ErrorData
) : RefuseReason;
