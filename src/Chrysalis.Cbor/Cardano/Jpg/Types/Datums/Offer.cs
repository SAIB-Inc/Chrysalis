using Chrysalis.Cbor.Attributes;
using Chrysalis.Cbor.Cardano.Jpg.Types.Common;
using Chrysalis.Cbor.Serialization.Converters.Primitives;
using Chrysalis.Cbor.Types;
using Chrysalis.Cbor.Types.Custom;
using Chrysalis.Cbor.Types.Primitives;

namespace Chrysalis.Cbor.Cardano.Jpg.Types.Datums;

[CborConverter(typeof(ConstrConverter))]
[CborOptions(Index = 0)]
public partial record Offer(
    [CborIndex(0)]
    CborBytes OwnerPkh,

    [CborIndex(1)]
    CborIndefList<OfferPayout> Payouts
) : CborBase;