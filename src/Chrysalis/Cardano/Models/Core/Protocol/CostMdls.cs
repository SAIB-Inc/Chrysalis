using Chrysalis.Cardano.Models.Cbor;
using Chrysalis.Cbor;

namespace Chrysalis.Cardano.Models.Core.Protocol;

public record CostMdls(Dictionary<CborInt, CborDefiniteList<CborUlong>> Value)
    : CborMap<CborInt, CborDefiniteList<CborUlong>>(Value), ICbor;