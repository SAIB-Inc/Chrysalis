using Chrysalis.Cbor.Cardano.Types.Block.Transaction.Output;

namespace Chrysalis.Cbor.Cardano.Extensions;

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
            PostAlonzoTransactionOutput e => e.Address,
            AlonzoTransactionOutput e => e.Address,
            _ => null
        };

    public static Value? Amount(this TransactionOutput transactionOutput)
        => transactionOutput switch
        {
            PostAlonzoTransactionOutput e => e.Amount,
            AlonzoTransactionOutput e => e.Amount,
            _ => null
        };

    public static ulong? Lovelace(this TransactionOutput output)
        => output.Amount()?.Lovelace();

    public static ulong? QuantityOf(this TransactionOutput output, string policyId, string assetName)
        => output.Amount()?.QuantityOf(policyId, assetName);

    public static byte[]? AddressValue(this TransactionOutput transactionOutput)
        => transactionOutput switch
        {
            PostAlonzoTransactionOutput e => e.Address.Value,
            AlonzoTransactionOutput e => e.Address.Value,
            _ => null
        };

    public static byte[]? ScriptRef(this TransactionOutput transactionOutput)
        => transactionOutput switch
        {
            PostAlonzoTransactionOutput e => e?.ScriptRef?.Value,
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
            PostAlonzoTransactionOutput b => b.Datum switch
            {
                InlineDatumOption inline => (DatumType.Inline, inline.Data.Value),
                DatumHashOption hash => (DatumType.Hash, hash.DatumHash.Value),
                _ => (DatumType.None, null)
            },
            _ => (DatumType.None, null)
        };
    }

       
}