using Chrysalis.Cardano.Models.Cbor;

namespace Chrysalis.Cardano.Models.Core;

public record Address(byte[] Value) : CborBytes(Value);
