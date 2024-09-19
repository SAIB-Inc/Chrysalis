using Chrysalis.Cardano.Models.Cbor;

namespace Chrysalis.Cardano.Models.Core;

public record RewardAccount(byte[] Value) : CborBytes(Value);