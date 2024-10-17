using Chrysalis.Cardano.Models.Cbor;
using Chrysalis.Cardano.Models.Core;
using Chrysalis.Cbor;
using Address = Chrysalis.Cardano.Models.Plutus.Address;

namespace Chrysalis.Cardano.Models.Jpeg;

[CborSerializable(CborType.Constr, Index = 0)]
public record Offer(
    [CborProperty(0)]
    CborBytes OwnerPkh,

    [CborProperty(1)]
    CborIndefiniteList<OfferPayout> Payouts
) : ICbor;

[CborSerializable(CborType.Constr, Index = 0)]
public record OfferPayout(
    [CborProperty(0)]
    Address Address,

    [CborProperty(1)]
    PayoutValue PayoutValue
) : ICbor;

public record PayoutValue(Dictionary<CborBytes, Token> Value) :
    CborMap<CborBytes, Token>(Value), ICbor;