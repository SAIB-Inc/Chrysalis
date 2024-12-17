using Chrysalis.Cardano.Core.Types.Block.Transaction.Body;
using Chrysalis.Cardano.Core.Types.Block.Transaction.Output;

namespace Chrysalis.Cardano.Core.Extensions;


public enum DatumType
{
    Inline,
    Hash,
    None
}

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

    public static ulong? Lovelace(this TransactionOutput output)
        => output.Amount()?.Lovelace();

    public static ulong? QuantityOf(this TransactionOutput output, string policyId, string assetName)
        => output.Amount()?.QuantityOf(policyId, assetName);

    public static byte[]? AddressValue(this TransactionOutput transactionOutput)
        => transactionOutput switch
        {
            BabbageTransactionOutput babbageTransactionOutput => babbageTransactionOutput.Address.Value,
            AlonzoTransactionOutput alonzoTransactionOutput => alonzoTransactionOutput.Address.Value,
            MaryTransactionOutput maryTransactionOutput => maryTransactionOutput.Address.Value,
            ShellyTransactionOutput shellyTransactionOutput => shellyTransactionOutput.Address.Value,
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
        (DatumType DatumType, byte[]? RawData) datum = transactionOutput.DatumInfo();

        return datum.RawData;
    }

    public static (DatumType DatumType, byte[]? RawData) DatumInfo(this TransactionOutput transactionOutput)
    {

        return transactionOutput switch
        {
            AlonzoTransactionOutput a => a.DatumHash switch
            {
                null => (DatumType.None, null),
                _ => (DatumType.Hash, a.DatumHash.Value)
            },
            BabbageTransactionOutput b => b.Datum switch
            {
                InlineDatumOption inline => (DatumType.Inline, inline.Data.Value),
                DatumHashOption hash => (DatumType.Hash, hash.DatumHash.Value),
                _ => (DatumType.None, null)
            },
            _ => (DatumType.None, null)
        };
    }
}