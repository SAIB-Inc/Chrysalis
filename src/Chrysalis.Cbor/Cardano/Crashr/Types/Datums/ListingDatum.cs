using Chrysalis.Cbor.Attributes;
using Chrysalis.Cbor.Cardano.Crashr.Types.Common;
using Chrysalis.Cbor.Serialization.Converters.Primitives;
using Chrysalis.Cbor.Types;
using Chrysalis.Cbor.Types.Primitives;

namespace Chrysalis.Cbor.Cardano.Crashr.Types.Datums;

[CborConverter(typeof(ConstrConverter))]
[CborOptions(Index = 1)]
public record ListingDatum(
  [CborIndex(0)] CborList<Payout> Payouts,
  [CborIndex(1)] CborBytes Owner
) : CborBase;