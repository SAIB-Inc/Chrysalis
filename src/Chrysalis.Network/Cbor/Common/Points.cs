using Chrysalis.Cbor.Attributes;


using Chrysalis.Cbor.Types;
using Chrysalis.Cbor.Types.Primitives;

namespace Chrysalis.Network.Cbor.Common;

[CborConverter(typeof(CustomListConverter))]
[CborOptions(IsDefinite = true)]
public partial record Point(
    [CborIndex(0)] CborUlong Slot,
    [CborIndex(1)] CborBytes Hash
) : CborBase;

[CborConverter(typeof(ListConverter))]
[CborOptions(IsDefinite = true)]
public partial record Points(List<Point> Value) : CborBase;