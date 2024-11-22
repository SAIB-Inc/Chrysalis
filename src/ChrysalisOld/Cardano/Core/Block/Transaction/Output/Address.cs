using Chrysalis.Cardano.Cbor;

namespace Chrysalis.Cardano.Core;

public record Address(byte[] Value) : CborBytes(Value);
