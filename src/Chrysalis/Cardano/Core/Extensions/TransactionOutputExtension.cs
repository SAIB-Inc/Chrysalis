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

    public static byte[] AddressValue(this TransactionOutput transactionOutput)
        => transactionOutput switch
        {
            BabbageTransactionOutput babbageTransactionOutput => babbageTransactionOutput.Address.Value,
            AlonzoTransactionOutput alonzoTransactionOutput => alonzoTransactionOutput.Address.Value,
            MaryTransactionOutput maryTransactionOutput => maryTransactionOutput.Address.Value,
            ShellyTransactionOutput shellyTransactionOutput => shellyTransactionOutput.Address.Value,
            _ => throw new NotImplementedException()
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
        byte[]? datumHash = transactionOutput.DatumHash();

        byte[]? rawData = datumOption switch
        {
            DatumHashOption hashOption => hashOption.DatumHash.Value,
            InlineDatumOption inlineOption => inlineOption.Data.Value,
            _ => null
        };

        rawData ??= datumHash;

        return rawData;
    }

    public static (DatumType DatumType, byte[]? RawData) DatumInfo(this TransactionOutput transactionOutput)
    {
        DatumOption? datumOption = transactionOutput.DatumOption();

        return datumOption switch
        {
            DatumHashOption hashOption => (DatumType.Hash, hashOption.DatumHash.Value),
            InlineDatumOption inlineOption => (DatumType.Inline, inlineOption.Data.Value),
            _ => (DatumType.None, null)
        };
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