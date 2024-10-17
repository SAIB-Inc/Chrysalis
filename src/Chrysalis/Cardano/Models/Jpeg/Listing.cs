using Chrysalis.Cardano.Models.Cbor;
using Chrysalis.Cardano.Models.Plutus;
using Chrysalis.Cbor;

namespace Chrysalis.Cardano.Models.Jpeg;

[CborSerializable(CborType.Constr, Index = 0)]
public record Listing(
    [CborProperty(0)]
    CborIndefiniteList<ListingPayout> Payouts,

    [CborProperty(1)]
    CborBytes OwnerPkh
) : ICbor;

[CborSerializable(CborType.Constr, Index = 0)]
public record ListingPayout(
    [CborProperty(0)]
    Address Address,

    [CborProperty(1)]
    CborUlong Amount
) : ICbor;