using Chrysalis.Cbor.Serialization.Attributes;
using Chrysalis.Cbor.Types;

namespace Chrysalis.Network.Cbor.Handshake;


[CborSerializable]
[CborUnion]
public partial record RefuseReason : CborBase;

[CborSerializable]
[CborList]
public partial record VersionMismatch(
    [CborOrder(0)] Value0 Idx,
    [CborOrder(1)] CborDefList<int> VersionNumbers
) : RefuseReason;

[CborSerializable]
[CborList]
public partial record HandshakeDecodeError(
    [CborOrder(0)] Value1 Idx,
    [CborOrder(1)] int VersionNumber,
    [CborOrder(2)] byte[] ErrorData
) : RefuseReason;

[CborSerializable]
[CborList]
public partial record Refused(
    [CborOrder(0)] Value2 Idx,
    [CborOrder(1)] int VersionNumber,
    [CborOrder(2)] byte[] ErrorData
) : RefuseReason;
