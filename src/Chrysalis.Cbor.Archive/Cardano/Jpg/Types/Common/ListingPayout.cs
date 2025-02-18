using Chrysalis.Cbor.Attributes;
using Chrysalis.Cbor.Converters.Primitives;
using Chrysalis.Cbor.Types;
using Chrysalis.Cbor.Types.Primitives;
using Chrysalis.Plutus.Types.Address;

namespace Chrysalis.Cardano.Jpg.Types.Common;

[CborConverter(typeof(ConstrConverter))]
[CborIndex(0)]
public record ListingPayout(
    [CborProperty(0)]
    Address Address,

    [CborProperty(1)]
    CborUlong Amount
) : CborBase;