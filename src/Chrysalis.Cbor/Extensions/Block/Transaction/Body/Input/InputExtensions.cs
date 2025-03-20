using Chrysalis.Cbor.Cardano.Types.Block.Transaction.Input;

namespace Chrysalis.Cbor.Extensions.Block.Transaction.Body.Input;

public static class InputExtensions
{
    public static byte[] TransactionId(this TransactionInput self) => self.TransactionId;

    public static ulong Index(this TransactionInput self) => self.Index;
}