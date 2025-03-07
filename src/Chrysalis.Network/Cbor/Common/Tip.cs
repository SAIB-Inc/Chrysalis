using Chrysalis.Cbor.Attributes;
using Chrysalis.Cbor.Serialization.Converters.Custom;
using Chrysalis.Cbor.Serialization.Converters.Primitives;
using Chrysalis.Cbor.Types;
using Chrysalis.Cbor.Types.Primitives;

namespace Chrysalis.Network.Cbor.Common;

[CborConverter(typeof(CustomListConverter))]
[CborOptions(IsDefinite = true)]
public record Tip(
    [CborIndex(0)] Point Point,
    [CborIndex(1)] CborInt BlockNumber
) : CborBase;
