using Chrysalis.Cardano.Core;

namespace Chrysalis.Utils;

public static class TransactionInputUtils
{
    public static string TransacationId(this TransactionInput transactionInput)
        => Convert.ToHexString(transactionInput.TransactionId.Value).ToLowerInvariant();

    public static ulong Index(this TransactionInput transactionInput)
        => transactionInput.Index.Value;

    public static byte[]? ScriptRef(this TransactionOutput transactionOutput)
        => transactionOutput switch
        {
            BabbageTransactionOutput babbageTransactionOutput => babbageTransactionOutput?.ScriptRef?.Value,
            _ => null
        };

    public static DatumOption? DatumOption(this TransactionOutput transactionOutput)
        => transactionOutput switch
        {
            BabbageTransactionOutput babbageTransactionOutput => babbageTransactionOutput.Datum,
            _ => null
        };

    public static byte[]? DatumHash(this TransactionOutput transactionOutput)
        => transactionOutput switch
        {
            AlonzoTransactionOutput alonzoTransactionOutput => alonzoTransactionOutput.DatumHash.Value,
            _ => null
        };

    public static byte[]? DatumInfo(this TransactionOutput transactionOutput)
    {
        var datumOption = transactionOutput.DatumOption();

        if (datumOption == null)
        {
            byte[]? datumHash = transactionOutput.DatumHash();
            return datumHash ?? null;
        }

        return datumOption switch
        {
            DatumHashOption hashOption => hashOption.DatumHash.Value,
            InlineDatumOption inlineOption => inlineOption.Data.Value,
            _ => null
        };
    }
}