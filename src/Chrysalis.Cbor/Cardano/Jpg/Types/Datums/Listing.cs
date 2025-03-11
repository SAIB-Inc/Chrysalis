
using Chrysalis.Cbor.Attributes;
using Chrysalis.Cbor.Cardano.Jpg.Types.Common;
using Chrysalis.Cbor.Serialization.Converters.Primitives;
using Chrysalis.Cbor.Types;
using Chrysalis.Cbor.Types.Custom;
using Chrysalis.Cbor.Types.Primitives;

namespace Chrysalis.Cbor.Cardano.Jpg.Types.Datums;

[CborConverter(typeof(ConstrConverter))]
[CborOptions(Index = 0)]
public partial record Listing(
    [CborIndex(0)]
    CborIndefList<ListingPayout> Payouts,

    [CborIndex(1)]
    CborBytes OwnerPkh
) : CborBase;


