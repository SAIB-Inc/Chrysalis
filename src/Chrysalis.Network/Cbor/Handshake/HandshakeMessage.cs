using Chrysalis.Cbor.Attributes;
using Chrysalis.Cbor.Serialization.Converters.Custom;
using Chrysalis.Cbor.Types;
using Chrysalis.Cbor.Types.Custom;
using Chrysalis.Cbor.Types.Primitives;

namespace Chrysalis.Network.Cbor.Handshake;

[CborConverter(typeof(UnionConverter))]
public abstract record HandshakeMessage : CborBase;

public class HandshakeMessages
{
    public static ProposeVersions ProposeVersions(VersionTable versionTable) =>
        new(new ExactValue<CborInt>(new(0)), versionTable);

    public static N2NAcceptVersion N2NAcceptVersion(N2NVersion version, N2NVersionData versionData) =>
        new(new ExactValue<CborInt>(new(1)), version, versionData);

    public static N2CAcceptVersion N2CAcceptVersion(N2CVersion version, N2CVersionData versionData) =>
        new(new ExactValue<CborInt>(new(1)), version, versionData);

    public static Refuse Refuse(RefuseReason reason) =>
        new(new ExactValue<CborInt>(new(2)), reason);

    public static N2NQueryReply N2NQueryReply(N2NVersionTable versionTable) =>
        new(new ExactValue<CborInt>(new(3)), versionTable);

    public static N2CQueryReply N2CQueryReply(N2CVersionTable versionTable) =>
        new(new ExactValue<CborInt>(new(3)), versionTable);
}

#region ProposeVersions
[CborConverter(typeof(CustomListConverter))]
[CborOptions(IsDefinite = true)]
public record ProposeVersions(
    [CborIndex(0)] [ExactValue(0)]
    ExactValue<CborInt> Idx,

    [CborIndex(1)] 
    VersionTable VersionTable
) : HandshakeMessage;

#endregion

#region AcceptVersions

[CborConverter(typeof(UnionConverter))]
public abstract record AcceptVersion : HandshakeMessage;


[CborConverter(typeof(CustomListConverter))]
[CborOptions(IsDefinite = true)]
public record N2NAcceptVersion(
    [CborIndex(0)] [ExactValue(1)] ExactValue<CborInt> Idx,
    [CborIndex(1)] N2NVersion Version,
    [CborIndex(2)] N2NVersionData VersionData
) : AcceptVersion;

[CborConverter(typeof(CustomListConverter))]
[CborOptions(IsDefinite = true)]
public record N2CAcceptVersion(
    [CborIndex(0)] [ExactValue(1)] ExactValue<CborInt> Idx,
    [CborIndex(1)] N2CVersion Version,
    [CborIndex(2)] N2CVersionData VersionData
) : AcceptVersion;


#endregion

#region Refuse
[CborConverter(typeof(CustomListConverter))]
[CborOptions(IsDefinite = true)]
public record Refuse(
    [CborIndex(0)] [ExactValue(2)] ExactValue<CborInt> Idx,
    [CborIndex(1)] RefuseReason Reason
) : HandshakeMessage;

#endregion

#region Query
[CborConverter(typeof(UnionConverter))]
public abstract record QueryReply : HandshakeMessage;

[CborConverter(typeof(CustomListConverter))]
[CborOptions(IsDefinite = true)]
public record N2NQueryReply(
    [CborIndex(0)] [ExactValue(3)] ExactValue<CborInt> Idx,
    [CborIndex(1)] N2NVersionTable VersionTable
) : QueryReply;

[CborConverter(typeof(CustomListConverter))]
[CborOptions(IsDefinite = true)]
public record N2CQueryReply(
    [CborIndex(0)] [ExactValue(3)] ExactValue<CborInt> Idx,
    [CborIndex(1)] N2CVersionTable VersionTable
) : QueryReply;


#endregion