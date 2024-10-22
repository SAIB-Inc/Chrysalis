using Chrysalis.Cbor;
using Chrysalis.Cardano.Models.Cbor;

namespace Chrysalis.Cardano.Models.Core.Block.Transaction.Body;

public record Withdrawals(Dictionary<RewardAccount, CborUlong> Value)
    : CborMap<RewardAccount, CborUlong>(Value), ICbor;