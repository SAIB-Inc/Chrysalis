using Chrysalis.Cbor.Attributes;
using Chrysalis.Cbor.Serialization.Converters.Primitives;
using Chrysalis.Cbor.Types;
using Chrysalis.Cbor.Types.Primitives;

namespace Chrysalis.Cbor.Cardano.Jpg.Types.Redeemers;

[CborConverter(typeof(ConstrConverter))]
[CborOptions(Index = 0)]
public record BuyRedeemer(
    [CborIndex(0)]
    CborInt Offset
) : CborBase;