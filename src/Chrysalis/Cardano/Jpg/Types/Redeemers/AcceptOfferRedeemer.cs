using Chrysalis.Cbor.Abstractions;
using Chrysalis.Cbor.Attributes;
using Chrysalis.Cbor.Converters.Primitives;

namespace Chrysalis.Cardano.Jpg.Types.Redeemers;

[CborConverter(typeof(ConstrConverter))]
[CborIndex(1)]
public record AcceptOfferRedeemer() : CborBase;