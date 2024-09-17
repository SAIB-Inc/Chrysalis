using Chrysalis.Cardano.Models.Cbor;
using Chrysalis.Cbor;

namespace Chrysalis.Cardano.Models.Core.Transaction;

public record Withdrawals(Dictionary<RewardAccount, CborUlong> Value)
    : CborMap<RewardAccount, CborUlong>(Value), ICbor;