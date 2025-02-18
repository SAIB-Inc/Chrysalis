using Chrysalis.Cbor.Attributes;
using Chrysalis.Cbor.Serialization.Converters.Primitives;
using Chrysalis.Cbor.Types;

namespace Chrysalis.Cbor.Cardano.Jpg.Types.Redeemers;

[CborConverter(typeof(ConstrConverter))]
[CborOptions(Index = 1)]
public record AcceptOfferRedeemer() : CborBase;