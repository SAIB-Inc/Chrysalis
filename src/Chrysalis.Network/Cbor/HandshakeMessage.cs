using Chrysalis.Cbor.Attributes;
using Chrysalis.Cbor.Serialization.Converters.Custom;
using Chrysalis.Cbor.Types;
using Chrysalis.Cbor.Types.Primitives;

namespace Chrysalis.Network.Cbor;

[CborConverter(typeof(UnionConverter))]
public abstract record HandshakeMessage : CborBase;

[CborConverter(typeof(CustomListConverter))]
[CborOptions(IsDefinite = true)]
public record ProposeVersions(
    [CborIndex(0)] ProposeVersionId MessageId,
    [CborIndex(1)] VersionTable VersionTable
) : HandshakeMessage;

[CborConverter(typeof(EnforcedIntConverter))]
[CborOptions(Index = 0)]
public record ProposeVersionId(int Value) : CborBase;

[CborConverter(typeof(CustomListConverter))]
[CborOptions(IsDefinite = true)]
public record AcceptVersion(
    [CborIndex(0)] CborInt AcceptVersionId,
    [CborIndex(1)] VersionNumber VersionNumber,
    [CborIndex(2)] NodeToNodeVersionData NodeToNodeVersionData
) : HandshakeMessage;

[CborConverter(typeof(EnforcedIntConverter))]
[CborOptions(Index = 1)]
public record AcceptVersionId(int Value) : CborBase;

[CborConverter(typeof(CustomListConverter))]
[CborOptions(IsDefinite = true)]
public record Refuse(
    [CborIndex(0)] RefuseVersionId MessageId,
    [CborIndex(1)] RefuseReason Reason
) : HandshakeMessage;

[CborConverter(typeof(EnforcedIntConverter))]
[CborOptions(Index = 2)]
public record RefuseVersionId(int Value) : CborBase;

[CborConverter(typeof(CustomListConverter))]
[CborOptions(IsDefinite = true)]
public record QueryReply(
    [CborIndex(0)] QueryVersionId MessageId,
    [CborIndex(1)] VersionTable VersionTable
) : HandshakeMessage;

[CborConverter(typeof(EnforcedIntConverter))]
[CborOptions(Index = 3)]
public record QueryVersionId(int Value) : CborBase;
