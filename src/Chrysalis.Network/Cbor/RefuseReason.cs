using Chrysalis.Cbor.Attributes;
using Chrysalis.Cbor.Serialization.Converters.Custom;
using Chrysalis.Cbor.Types;
using Chrysalis.Cbor.Types.Custom;
using Chrysalis.Cbor.Types.Primitives;

namespace Chrysalis.Network.Cbor;

[CborConverter(typeof(UnionConverter))]
public record RefuseReason : CborBase;

[CborConverter(typeof(CustomListConverter))]
[CborOptions(IsDefinite = true)]
public record VersionMismatch(
    [CborIndex(0)] VersionMismatchId ReasonId,
    [CborIndex(1)] CborDefList<CborInt> VersionNumbers
) : RefuseReason;

[CborConverter(typeof(CustomListConverter))]
[CborOptions(IsDefinite = true)]
public record HandshakeDecodeError(
    [CborIndex(0)] HandshakeDecodeErrorId ReasonId,
    [CborIndex(1)] CborInt VersionNumber,
    [CborIndex(2)] CborBytes ErrorData
) : RefuseReason;

[CborConverter(typeof(CustomListConverter))]
[CborOptions(IsDefinite = true)]
public record Refused(
    [CborIndex(0)] RefusedId ReasonId,
    [CborIndex(1)] CborInt VersionNumber,
    [CborIndex(2)] CborBytes ErrorData
) : RefuseReason;

[CborConverter(typeof(EnforcedIntConverter))]
[CborOptions(Index = 0)]
public record VersionMismatchId(int Value) : CborBase;

[CborConverter(typeof(EnforcedIntConverter))]
[CborOptions(Index = 1)]
public record HandshakeDecodeErrorId(int Value) : CborBase;

[CborConverter(typeof(EnforcedIntConverter))]
[CborOptions(Index = 2)]
public record RefusedId(int Value) : CborBase;