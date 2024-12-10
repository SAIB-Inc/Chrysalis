using Chrysalis.Cbor.Attributes;
using Chrysalis.Cbor.Converters.Primitives;
using Chrysalis.Cbor.Types;
using Chrysalis.Cbor.Types.Primitives;
using Chrysalis.Plutus.Types.Address;

namespace Chrysalis.Cardano.Jpg.Types.Common;

[CborConverter(typeof(ConstrConverter))]
[CborIndex(0)]
public record OfferPayout(
    [CborProperty(0)]
    Address Address,

    [CborProperty(1)]
    PayoutValue PayoutValue
) : CborBase;

[CborConverter(typeof(MapConverter))]
public record PayoutValue(Dictionary<CborBytes, Token> Value) : CborBase;