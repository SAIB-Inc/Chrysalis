using Chrysalis.Cardano.Cbor;
using Chrysalis.Cardano.Plutus;
using Chrysalis.Cbor;

namespace Chrysalis.Cardano.Jpeg;

[CborSerializable(CborType.Constr, Index = 0)]
public record Listing(
    [CborProperty(0)]
    CborIndefiniteList<ListingPayout> Payouts,

    [CborProperty(1)]
    CborBytes OwnerPkh
) : RawCbor;

[CborSerializable(CborType.Constr, Index = 0)]
public record ListingPayout(
    [CborProperty(0)]
    Address Address,

    [CborProperty(1)]
    CborUlong Amount
) : RawCbor;