using Chrysalis.Cardano.Models.Cbor;

namespace Chrysalis.Cardano.Models.Core.Block.Transaction.Body;

public record RewardAccount(byte[] Value) : CborBytes(Value);