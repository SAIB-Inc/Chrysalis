using Chrysalis.Cbor.Attributes;
using Chrysalis.Cbor.Serialization.Converters.Custom;
using Chrysalis.Cbor.Types;
using Chrysalis.Cbor.Types.Primitives;

namespace Chrysalis.Network.Cbor;

[CborConverter(typeof(UnionConverter))]
public abstract record HandshakeMessage : CborBase;

#region ProposeVersions
[CborConverter(typeof(UnionConverter))]
public abstract record ProposeVersion : HandshakeMessage;

[CborConverter(typeof(CustomListConverter))]
[CborOptions(IsDefinite = true)]
public record N2NProposeVersions(
    [CborIndex(0)] ProposeVersionIdx Idx,
    [CborIndex(1)] N2NVersionTable VersionTable
) : ProposeVersion;

[CborConverter(typeof(CustomListConverter))]
[CborOptions(IsDefinite = true)]
public record N2CProposeVersions(
    [CborIndex(0)] ProposeVersionIdx Idx,
    [CborIndex(1)] N2CVersionTable VersionTable
) : ProposeVersion;

[CborConverter(typeof(EnforcedIntConverter))]
[CborOptions(Index = 0)]
public record ProposeVersionIdx(int Value) : CborBase;

#endregion

#region AcceptVersions

[CborConverter(typeof(UnionConverter))]
public abstract record AcceptVersion : HandshakeMessage;


[CborConverter(typeof(CustomListConverter))]
[CborOptions(IsDefinite = true)]
public record N2NAcceptVersion(
    [CborIndex(0)] CborInt AcceptVersionIdx,
    [CborIndex(1)] N2NVersion Version,
    [CborIndex(2)] N2NVersionData VersionData
) : AcceptVersion;

[CborConverter(typeof(CustomListConverter))]
[CborOptions(IsDefinite = true)]
public record N2CAcceptVersion(
    [CborIndex(0)] CborInt AcceptVersionIdx,
    [CborIndex(1)] N2CVersion Version,
    [CborIndex(2)] N2CVersionData VersionData
) : AcceptVersion;

[CborConverter(typeof(EnforcedIntConverter))]
[CborOptions(Index = 1)]
public record AcceptVersionIdx(int Value) : CborBase;

#endregion

#region Refuse
[CborConverter(typeof(CustomListConverter))]
[CborOptions(IsDefinite = true)]
public record Refuse(
    [CborIndex(0)] RefuseIdx Idx,
    [CborIndex(1)] RefuseReason Reason
) : HandshakeMessage;

[CborConverter(typeof(EnforcedIntConverter))]
[CborOptions(Index = 2)]
public record RefuseIdx(int Value) : CborBase;

#endregion

#region Query
[CborConverter(typeof(UnionConverter))]
public abstract record QueryReply : HandshakeMessage;

[CborConverter(typeof(CustomListConverter))]
[CborOptions(IsDefinite = true)]
public record N2NQueryReply(
    [CborIndex(0)] QueryReplyIdx Idx,
    [CborIndex(1)] N2NVersionTable VersionTable
) : QueryReply;

[CborConverter(typeof(CustomListConverter))]
[CborOptions(IsDefinite = true)]
public record N2CQueryReply(
    [CborIndex(0)] QueryReplyIdx Idx,
    [CborIndex(1)] N2CVersionTable VersionTable
) : QueryReply;

[CborConverter(typeof(EnforcedIntConverter))]
[CborOptions(Index = 3)]
public record QueryReplyIdx(int Value) : CborBase;

#endregion