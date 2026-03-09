using Chrysalis.Codec.Serialization.Attributes;
using Chrysalis.Codec.Types;

namespace Chrysalis.Network.Cbor.Handshake;

/// <summary>
/// Base type for all Ouroboros Handshake mini-protocol CBOR messages.
/// </summary>
[CborSerializable]
[CborUnion]
public abstract partial record HandshakeMessage : CborBase;

/// <summary>
/// Factory methods for constructing Ouroboros Handshake mini-protocol messages with correct CBOR indices.
/// </summary>
public static class HandshakeMessages
{
    /// <summary>
    /// Creates a <see cref="Handshake.ProposeVersions"/> message proposing supported protocol versions to the remote peer.
    /// </summary>
    /// <param name="versionTable">The table of supported versions and their parameters.</param>
    /// <returns>A new <see cref="Handshake.ProposeVersions"/> with the correct CBOR index.</returns>
    public static ProposeVersions ProposeVersions(VersionTable versionTable)
    {
        return new(0, versionTable);
    }

    /// <summary>
    /// Creates a node-to-node <see cref="N2NAcceptVersion"/> message accepting a specific protocol version.
    /// </summary>
    /// <param name="version">The accepted node-to-node protocol version.</param>
    /// <param name="versionData">The parameters for the accepted version.</param>
    /// <returns>A new <see cref="N2NAcceptVersion"/> with the correct CBOR index.</returns>
    public static N2NAcceptVersion N2NAcceptVersion(N2NVersion version, N2NVersionData versionData)
    {
        return new(1, version, versionData);
    }

    /// <summary>
    /// Creates a node-to-client <see cref="N2CAcceptVersion"/> message accepting a specific protocol version.
    /// </summary>
    /// <param name="version">The accepted node-to-client protocol version.</param>
    /// <param name="versionData">The parameters for the accepted version.</param>
    /// <returns>A new <see cref="N2CAcceptVersion"/> with the correct CBOR index.</returns>
    public static N2CAcceptVersion N2CAcceptVersion(N2CVersion version, N2CVersionData versionData)
    {
        return new(1, version, versionData);
    }

    /// <summary>
    /// Creates a <see cref="Handshake.Refuse"/> message refusing the handshake with a specified reason.
    /// </summary>
    /// <param name="reason">The reason the handshake was refused.</param>
    /// <returns>A new <see cref="Handshake.Refuse"/> with the correct CBOR index.</returns>
    public static Refuse Refuse(RefuseReason reason)
    {
        return new(2, reason);
    }

    /// <summary>
    /// Creates a node-to-node <see cref="N2NQueryReply"/> message containing the server's supported version table.
    /// </summary>
    /// <param name="versionTable">The server's supported node-to-node versions.</param>
    /// <returns>A new <see cref="N2NQueryReply"/> with the correct CBOR index.</returns>
    public static N2NQueryReply N2NQueryReply(N2NVersionTable versionTable)
    {
        return new(3, versionTable);
    }

    /// <summary>
    /// Creates a node-to-client <see cref="N2CQueryReply"/> message containing the server's supported version table.
    /// </summary>
    /// <param name="versionTable">The server's supported node-to-client versions.</param>
    /// <returns>A new <see cref="N2CQueryReply"/> with the correct CBOR index.</returns>
    public static N2CQueryReply N2CQueryReply(N2CVersionTable versionTable)
    {
        return new(3, versionTable);
    }
}

#region ProposeVersions
/// <summary>
/// Handshake message proposing a set of supported protocol versions to the remote peer (MsgProposeVersions).
/// </summary>
/// <param name="Idx">The CBOR message index identifier (always 0 for this message type).</param>
/// <param name="VersionTable">The table of supported protocol versions and their parameters.</param>
[CborSerializable]
[CborList]
[CborIndex(0)]
public partial record ProposeVersions(
    [CborOrder(0)] int Idx,
    [CborOrder(1)] VersionTable VersionTable
) : HandshakeMessage;

#endregion

#region AcceptVersions

/// <summary>
/// Handshake response accepting a node-to-node protocol version (MsgAcceptVersion for N2N).
/// </summary>
/// <param name="Idx">The CBOR message index identifier (always 1 for this message type).</param>
/// <param name="Version">The accepted node-to-node protocol version.</param>
/// <param name="VersionData">The parameters associated with the accepted version.</param>
[CborSerializable]
[CborList]
[CborIndex(1)]
public partial record N2NAcceptVersion(
    [CborOrder(0)] int Idx,
    [CborOrder(1)] N2NVersion Version,
    [CborOrder(2)] N2NVersionData VersionData
) : HandshakeMessage;

/// <summary>
/// Handshake response accepting a node-to-client protocol version (MsgAcceptVersion for N2C).
/// </summary>
/// <param name="Idx">The CBOR message index identifier (always 1 for this message type).</param>
/// <param name="Version">The accepted node-to-client protocol version.</param>
/// <param name="VersionData">The parameters associated with the accepted version.</param>
[CborSerializable]
[CborList]
[CborIndex(1)]
public partial record N2CAcceptVersion(
    [CborOrder(0)] int Idx,
    [CborOrder(1)] N2CVersion Version,
    [CborOrder(2)] N2CVersionData VersionData
) : HandshakeMessage;


#endregion

#region Refuse
/// <summary>
/// Handshake response refusing the connection with a specified reason (MsgRefuse).
/// </summary>
/// <param name="Idx">The CBOR message index identifier (always 2 for this message type).</param>
/// <param name="Reason">The reason the handshake was refused.</param>
[CborSerializable]
[CborList]
[CborIndex(2)]
public partial record Refuse(
    [CborOrder(0)] int Idx,
    [CborOrder(1)] RefuseReason Reason
) : HandshakeMessage;

#endregion

#region Query

/// <summary>
/// Handshake query reply containing the server's supported node-to-node versions (MsgQueryReply for N2N).
/// </summary>
/// <param name="Idx">The CBOR message index identifier (always 3 for this message type).</param>
/// <param name="VersionTable">The server's supported node-to-node protocol versions and their parameters.</param>
[CborSerializable]
[CborList]
[CborIndex(3)]
public partial record N2NQueryReply(
    [CborOrder(0)] int Idx,
    [CborOrder(1)] N2NVersionTable VersionTable
) : HandshakeMessage;

/// <summary>
/// Handshake query reply containing the server's supported node-to-client versions (MsgQueryReply for N2C).
/// </summary>
/// <param name="Idx">The CBOR message index identifier (always 3 for this message type).</param>
/// <param name="VersionTable">The server's supported node-to-client protocol versions and their parameters.</param>
[CborSerializable]
[CborList]
[CborIndex(3)]
public partial record N2CQueryReply(
    [CborOrder(0)] int Idx,
    [CborOrder(1)] N2CVersionTable VersionTable
) : HandshakeMessage;


#endregion
