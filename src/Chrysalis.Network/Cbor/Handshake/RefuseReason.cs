using Chrysalis.Cbor.Attributes;
using Chrysalis.Cbor.Serialization.Converters.Custom;
using Chrysalis.Cbor.Types;
using Chrysalis.Cbor.Types.Custom;
using Chrysalis.Cbor.Types.Primitives;

namespace Chrysalis.Network.Cbor.Handshake;


[CborConverter(typeof(UnionConverter))]
public record RefuseReason : CborBase;

[CborConverter(typeof(CustomListConverter))]
[CborOptions(IsDefinite = true)]
public record VersionMismatch(
    [CborIndex(0)] [ExactValue(0)] ExactValue<CborInt> Idx,
    [CborIndex(1)] CborDefList<CborInt> VersionNumbers
) : RefuseReason;

[CborConverter(typeof(CustomListConverter))]
[CborOptions(IsDefinite = true)]
public record HandshakeDecodeError(
    [CborIndex(0)] [ExactValue(1)] ExactValue<CborInt> Idx,
    [CborIndex(1)] CborInt VersionNumber,
    [CborIndex(2)] CborBytes ErrorData
) : RefuseReason;

[CborConverter(typeof(CustomListConverter))]
[CborOptions(IsDefinite = true)]
public record Refused(
    [CborIndex(0)] [ExactValue(2)] ExactValue<CborInt> Idx,
    [CborIndex(1)] CborInt VersionNumber,
    [CborIndex(2)] CborBytes ErrorData
) : RefuseReason;
