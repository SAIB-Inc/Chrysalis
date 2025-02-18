
using Chrysalis.Cbor.Attributes;
using Chrysalis.Cbor.Converters.Primitives;
using Chrysalis.Cbor.Types.Collections;
using Chrysalis.Cbor.Types;
using Chrysalis.Cbor.Types.Primitives;
using Chrysalis.Cardano.Jpg.Types.Common;

namespace Chrysalis.Cardano.Jpg.Types.Datums;

[CborConverter(typeof(ConstrConverter))]
[CborIndex(0)]
public record Listing(
    [CborProperty(0)]
    CborIndefList<ListingPayout> Payouts,

    [CborProperty(1)]
    CborBytes OwnerPkh
) : CborBase;


