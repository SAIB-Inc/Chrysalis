using Chrysalis.Cardano.Core.Types.Block.Transaction.Input;

namespace Chrysalis.Cardano.Core.Extensions;

public static class TransactionInputExtension
{
    public static string TransactionId(this TransactionInput transactionInput)
        => Convert.ToHexString(transactionInput.TransactionId.Value).ToLowerInvariant();

    public static ulong Index(this TransactionInput transactionInput)
        => transactionInput.Index.Value;
}