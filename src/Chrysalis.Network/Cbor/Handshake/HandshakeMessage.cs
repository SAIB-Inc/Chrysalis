using Chrysalis.Cbor.Serialization.Attributes;
using Chrysalis.Cbor.Types;

namespace Chrysalis.Network.Cbor.Handshake;

[CborSerializable]
[CborUnion]
public abstract partial record HandshakeMessage : CborBase;

public static class HandshakeMessages
{
    public static ProposeVersions ProposeVersions(VersionTable versionTable)
    {
        return new(0, versionTable);
    }

    public static N2NAcceptVersion N2NAcceptVersion(N2NVersion version, N2NVersionData versionData)
    {
        return new(1, version, versionData);
    }

    public static N2CAcceptVersion N2CAcceptVersion(N2CVersion version, N2CVersionData versionData)
    {
        return new(1, version, versionData);
    }

    public static Refuse Refuse(RefuseReason reason)
    {
        return new(2, reason);
    }

    public static N2NQueryReply N2NQueryReply(N2NVersionTable versionTable)
    {
        return new(3, versionTable);
    }

    public static N2CQueryReply N2CQueryReply(N2CVersionTable versionTable)
    {
        return new(3, versionTable);
    }
}

#region ProposeVersions
[CborSerializable]
[CborList]
[CborIndex(0)]
public partial record ProposeVersions(
    [CborOrder(0)] int Idx,
    [CborOrder(1)] VersionTable VersionTable
) : HandshakeMessage;

#endregion

#region AcceptVersions

[CborSerializable]
[CborList]
[CborIndex(1)]
public partial record N2NAcceptVersion(
    [CborOrder(0)] int Idx,
    [CborOrder(1)] N2NVersion Version,
    [CborOrder(2)] N2NVersionData VersionData
) : HandshakeMessage;

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
[CborSerializable]
[CborList]
[CborIndex(2)]
public partial record Refuse(
    [CborOrder(0)] int Idx,
    [CborOrder(1)] RefuseReason Reason
) : HandshakeMessage;

#endregion

#region Query

[CborSerializable]
[CborList]
[CborIndex(3)]
public partial record N2NQueryReply(
    [CborOrder(0)] int Idx,
    [CborOrder(1)] N2NVersionTable VersionTable
) : HandshakeMessage;

[CborSerializable]
[CborList]
[CborIndex(3)]
public partial record N2CQueryReply(
    [CborOrder(0)] int Idx,
    [CborOrder(1)] N2CVersionTable VersionTable
) : HandshakeMessage;


#endregion
