using Chrysalis.Cardano.Crashr.Types.Common;
using Chrysalis.Cbor.Attributes;
using Chrysalis.Cbor.Converters.Primitives;
using Chrysalis.Cbor.Types;
using Chrysalis.Cbor.Types.Collections;
using Chrysalis.Cbor.Types.Primitives;

namespace Chrysalis.Cardano.Crashr.Types.Datums;

[CborConverter(typeof(ConstrConverter))]
[CborIndex(0)]
public record ListingDatum(
  [CborProperty(0)] CborList<Payout> Payouts,
  [CborProperty(1)] CborBytes Owner
) : CborBase;