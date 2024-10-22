using Chrysalis.Cardano.Models.Cbor;

namespace Chrysalis.Cardano.Models.Core.Block.Transaction.Output;

public record Address(byte[] Value) : CborBytes(Value);
