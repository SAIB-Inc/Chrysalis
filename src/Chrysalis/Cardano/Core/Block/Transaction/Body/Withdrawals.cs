using Chrysalis.Cbor;
using Chrysalis.Cardano.Cbor;

namespace Chrysalis.Cardano.Core;

public record Withdrawals(Dictionary<RewardAccount, CborUlong> Value)
    : CborMap<RewardAccount, CborUlong>(Value), ICbor;