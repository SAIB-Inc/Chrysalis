using Chrysalis.Cbor.Cardano.Types.Block.Transaction.Input;

namespace Chrysalis.Cbor.Cardano.Extensions;

public static class TransactionInputExtension
{
    public static string TransactionId(this TransactionInput transactionInput)
        => Convert.ToHexString(transactionInput.TransactionId.Value).ToLowerInvariant();

    public static ulong Index(this TransactionInput transactionInput)
        => transactionInput.Index.Value;

    public static string OutRef(this TransactionInput transactionInput)
        => $"{transactionInput.TransactionId()}{transactionInput.Index()}";
}
