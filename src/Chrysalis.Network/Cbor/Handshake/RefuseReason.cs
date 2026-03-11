using Chrysalis.Codec.Serialization.Attributes;
using Chrysalis.Codec.Types;

namespace Chrysalis.Network.Cbor.Handshake;


/// <summary>
/// Union type representing the reason a Handshake mini-protocol connection was refused.
/// </summary>
[CborSerializable]
[CborUnion]
public partial record RefuseReason : CborRecord;

/// <summary>
/// Handshake refusal indicating no mutually supported protocol version was found.
/// </summary>
/// <param name="Idx">The CBOR message index identifier (always 0 for this refuse reason type).</param>
/// <param name="VersionNumbers">The list of version numbers supported by the refusing peer.</param>
[CborSerializable]
[CborList]
[CborIndex(0)]
public partial record VersionMismatch(
    [CborOrder(0)] int Idx,
    [CborOrder(1)] CborDefList<int> VersionNumbers
) : RefuseReason;

/// <summary>
/// Handshake refusal indicating the version data for the proposed version could not be decoded.
/// </summary>
/// <param name="Idx">The CBOR message index identifier (always 1 for this refuse reason type).</param>
/// <param name="VersionNumber">The protocol version number that caused the decode error.</param>
/// <param name="ErrorData">The raw CBOR error data describing the decode failure.</param>
[CborSerializable]
[CborList]
[CborIndex(1)]
public partial record HandshakeDecodeError(
    [CborOrder(0)] int Idx,
    [CborOrder(1)] int VersionNumber,
    [CborOrder(2)] ReadOnlyMemory<byte> ErrorData
) : RefuseReason;

/// <summary>
/// Handshake refusal indicating the proposed version was explicitly refused by the peer.
/// </summary>
/// <param name="Idx">The CBOR message index identifier (always 2 for this refuse reason type).</param>
/// <param name="VersionNumber">The protocol version number that was refused.</param>
/// <param name="ErrorData">The raw CBOR error data describing the refusal reason.</param>
[CborSerializable]
[CborList]
[CborIndex(2)]
public partial record Refused(
    [CborOrder(0)] int Idx,
    [CborOrder(1)] int VersionNumber,
    [CborOrder(2)] ReadOnlyMemory<byte> ErrorData
) : RefuseReason;
