using Chrysalis.Cardano.Cbor;
using Chrysalis.Cbor;

namespace Chrysalis.Cardano.Core;

public record CostMdls(Dictionary<CborInt, CborDefiniteList<CborUlong>> Value)
    : CborMap<CborInt, CborDefiniteList<CborUlong>>(Value), ICbor;