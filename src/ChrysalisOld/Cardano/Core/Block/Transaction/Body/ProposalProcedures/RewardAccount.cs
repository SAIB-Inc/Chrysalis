using Chrysalis.Cardano.Cbor;

namespace Chrysalis.Cardano.Core;

public record RewardAccount(byte[] Value) : CborBytes(Value);