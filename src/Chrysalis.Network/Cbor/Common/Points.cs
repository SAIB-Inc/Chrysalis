using Chrysalis.Cbor.Attributes;
using Chrysalis.Cbor.Serialization.Converters.Custom;
using Chrysalis.Cbor.Serialization.Converters.Primitives;
using Chrysalis.Cbor.Types;
using Chrysalis.Cbor.Types.Primitives;

namespace Chrysalis.Network.Cbor.Common;

[CborConverter(typeof(CustomListConverter))]
[CborOptions(IsDefinite = true)]
public record Point(
    [CborIndex(0)] CborUlong Slot,
    [CborIndex(1)] CborBytes Hash
) : CborBase;

[CborConverter(typeof(ListConverter))]
[CborOptions(IsDefinite = true)]
public record Points(List<Point> Value) : CborBase;