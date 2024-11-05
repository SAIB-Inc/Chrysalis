using Chrysalis.Cardano.Cbor;
using Chrysalis.Cardano.Core;
using Chrysalis.Cbor;
using Address = Chrysalis.Cardano.Plutus.Address;

namespace Chrysalis.Cardano.Jpeg;

[CborSerializable(CborType.Constr, Index = 0)]
public record Offer(
    [CborProperty(0)]
    CborBytes OwnerPkh,

    [CborProperty(1)]
    CborIndefiniteList<OfferPayout> Payouts
) : RawCbor;

[CborSerializable(CborType.Constr, Index = 0)]
public record OfferPayout(
    [CborProperty(0)]
    Address Address,

    [CborProperty(1)]
    PayoutValue PayoutValue
) : RawCbor;

public record PayoutValue(Dictionary<CborBytes, Token> Value) :
    CborMap<CborBytes, Token>(Value), ICbor;