using Chrysalis.Cardano.Core;
using Chrysalis.Cardano.Core.Types.Block.Transaction.Output;

namespace Chrysalis.Extensions;

public static class TransactionOutputExtension
{
    public static Address? Address(this TransactionOutput transactionOutput)
        => transactionOutput switch
        {
            BabbageTransactionOutput babbageTransactionOutput => babbageTransactionOutput.Address,
            AlonzoTransactionOutput alonzoTransactionOutput => alonzoTransactionOutput.Address,
            MaryTransactionOutput maryTransactionOutput => maryTransactionOutput.Address,
            ShellyTransactionOutput shellyTransactionOutput => shellyTransactionOutput.Address,
            _ => null
        };

    public static Value? Amount(this TransactionOutput transactionOutput)
        => transactionOutput switch
        {
            BabbageTransactionOutput babbageTransactionOutput => babbageTransactionOutput.Amount,
            AlonzoTransactionOutput alonzoTransactionOutput => alonzoTransactionOutput.Amount,
            MaryTransactionOutput maryTransactionOutput => maryTransactionOutput.Amount,
            ShellyTransactionOutput shellyTransactionOutput => shellyTransactionOutput.Amount,
            _ => null
        };

    public static byte[]? ScriptRef(this TransactionOutput transactionOutput)
        => transactionOutput switch
        {
            BabbageTransactionOutput babbageTransactionOutput => babbageTransactionOutput?.ScriptRef?.Value,
            _ => null
        };

    public static byte[]? Datum(this TransactionOutput transactionOutput)
    {
        DatumOption? datumOption = transactionOutput.DatumOption();

        if (datumOption == null)
        {
            return transactionOutput.DatumHash();
        }

        return datumOption.DatumHash() ?? datumOption.InlineDatum();
    }

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
}