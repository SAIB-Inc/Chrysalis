using Chrysalis.Cbor.Types.Cardano.Core.Transaction;

namespace Chrysalis.Cbor.Extensions.Cardano.Core.Transaction.Body.Input;

public static class InputExtensions
{
    public static byte[] TransactionId(this TransactionInput self) => self.TransactionId;

    public static ulong Index(this TransactionInput self) => self.Index;
}