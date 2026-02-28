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
        return new(new Value0(0), versionTable);
    }

    public static N2NAcceptVersion N2NAcceptVersion(N2NVersion version, N2NVersionData versionData)
    {
        return new(new Value1(1), version, versionData);
    }

    public static N2CAcceptVersion N2CAcceptVersion(N2CVersion version, N2CVersionData versionData)
    {
        return new(new Value1(1), version, versionData);
    }

    public static Refuse Refuse(RefuseReason reason)
    {
        return new(new Value2(2), reason);
    }

    public static N2NQueryReply N2NQueryReply(N2NVersionTable versionTable)
    {
        return new(new Value3(3), versionTable);
    }

    public static N2CQueryReply N2CQueryReply(N2CVersionTable versionTable)
    {
        return new(new Value3(3), versionTable);
    }
}

#region ProposeVersions
[CborSerializable]
[CborList]
public partial record ProposeVersions(
    [CborOrder(0)] Value0 Idx,
    [CborOrder(1)] VersionTable VersionTable
) : HandshakeMessage;

#endregion

#region AcceptVersions

[CborSerializable]
[CborUnion]
public abstract partial record AcceptVersion : HandshakeMessage;


[CborSerializable]
[CborList]
public partial record N2NAcceptVersion(
    [CborOrder(0)] Value1 Idx,
    [CborOrder(1)] N2NVersion Version,
    [CborOrder(2)] N2NVersionData VersionData
) : AcceptVersion;

[CborSerializable]
[CborList]
public partial record N2CAcceptVersion(
    [CborOrder(0)] Value1 Idx,
    [CborOrder(1)] N2CVersion Version,
    [CborOrder(2)] N2CVersionData VersionData
) : AcceptVersion;


#endregion

#region Refuse
[CborSerializable]
[CborList]
public partial record Refuse(
    [CborOrder(0)] Value2 Idx,
    [CborOrder(1)] RefuseReason Reason
) : HandshakeMessage;

#endregion

#region Query
[CborSerializable]
[CborUnion]
public abstract partial record QueryReply : HandshakeMessage;

[CborSerializable]
[CborList]
public partial record N2NQueryReply(
    [CborOrder(0)] Value3 Idx,
    [CborOrder(1)] N2NVersionTable VersionTable
) : QueryReply;

[CborSerializable]
[CborList]
public partial record N2CQueryReply(
    [CborOrder(0)] Value3 Idx,
    [CborOrder(1)] N2CVersionTable VersionTable
) : QueryReply;


#endregion
