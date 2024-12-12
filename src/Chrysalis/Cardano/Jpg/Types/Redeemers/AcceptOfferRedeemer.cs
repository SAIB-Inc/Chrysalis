using Chrysalis.Cbor.Attributes;
using Chrysalis.Cbor.Converters.Primitives;
using Chrysalis.Cbor.Types;

namespace Chrysalis.Cardano.Jpg.Types.Redeemers;

[CborConverter(typeof(ConstrConverter))]
[CborIndex(1)]
public record AcceptOfferRedeemer() : CborBase;