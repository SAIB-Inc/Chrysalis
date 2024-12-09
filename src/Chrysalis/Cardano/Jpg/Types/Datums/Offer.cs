using Chrysalis.Cardano.Core.Types.Block.Transaction.Output;
using Chrysalis.Cbor.Attributes;
using Chrysalis.Cbor.Converters.Primitives;
using Chrysalis.Cbor.Types;
using Chrysalis.Cbor.Types.Collections;
using Chrysalis.Cbor.Types.Primitives;
using Chrysalis.Cardano.Jpg.Types.Common;

namespace Chrysalis.Cardano.Jpg.Types.Datums;

[CborConverter(typeof(ConstrConverter))]
[CborIndex(0)]
public record Offer(
    [CborProperty(0)]
    CborBytes OwnerPkh,

    [CborProperty(1)]
    CborIndefList<OfferPayout> Payouts
) : CborBase;