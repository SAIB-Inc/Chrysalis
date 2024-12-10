using Chrysalis.Cbor.Attributes;
using Chrysalis.Cbor.Converters.Primitives;
using Chrysalis.Cbor.Types;
using Chrysalis.Cbor.Types.Primitives;

namespace Chrysalis.Cardano.Jpg.Types.Redeemers;

[CborConverter(typeof(ConstrConverter))]
[CborIndex(0)]
public record BuyRedeemer(
    [CborProperty(0)]
    CborInt Offset
) : CborBase;