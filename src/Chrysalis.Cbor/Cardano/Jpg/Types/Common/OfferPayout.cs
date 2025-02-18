using Chrysalis.Cbor.Attributes;
using Chrysalis.Cbor.Plutus.Types.Address;
using Chrysalis.Cbor.Serialization.Converters.Primitives;
using Chrysalis.Cbor.Types;
using Chrysalis.Cbor.Types.Primitives;

namespace Chrysalis.Cbor.Cardano.Jpg.Types.Common;

[CborConverter(typeof(ConstrConverter))]
[CborOptions(Index = 0)]
public record OfferPayout(
    [CborIndex(0)]
    Address Address,

    [CborIndex(1)]
    PayoutValue PayoutValue
) : CborBase;

[CborConverter(typeof(MapConverter))]
public record PayoutValue(Dictionary<CborBytes, Token> Value) : CborBase;