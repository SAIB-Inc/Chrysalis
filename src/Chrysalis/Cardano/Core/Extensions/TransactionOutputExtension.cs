using Chrysalis.Cardano.Core.Types.Block.Transaction.Output;

namespace Chrysalis.Cardano.Core.Extensions;

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

    public static byte[] AddressValue(this TransactionOutput transactionOutput)
        => transactionOutput switch
    {
            BabbageTransactionOutput babbageTransactionOutput => babbageTransactionOutput.Address.Value,
            AlonzoTransactionOutput alonzoTransactionOutput => alonzoTransactionOutput.Address.Value,
            MaryTransactionOutput maryTransactionOutput => maryTransactionOutput.Address.Value,
            ShellyTransactionOutput shellyTransactionOutput => shellyTransactionOutput.Address.Value,
            _ => throw new NotImplementedException()
        };
    // public static Dictionary<string, ulong>? AssetsByPolicy(this TransactionOutput output, string policyId)
    //     => output.Amount()?.AssetsByPolicy(policyId);

    // public static bool HasMultipleAssets(this TransactionOutput output)
    //     => output.Amount()?.HasMultipleAssets() ?? false;

    // public static IEnumerable<string>? PolicyIds(this TransactionOutput output)
    //     => output.Amount()?.PolicyIds();
        
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