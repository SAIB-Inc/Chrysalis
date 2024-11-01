using Chrysalis.Cardano.Core;

namespace Chrysalis.Utils;

public static class TransactionUtils
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

    public static MultiAssetOutput? MultiAsset(this Value value)
        => value switch
        {
            LovelaceWithMultiAsset lovelaceWithMultiAsset => lovelaceWithMultiAsset.MultiAsset,
            _ => null
        };

    public static ulong? GetCoin(this Value value)
        => value switch
        {
            Lovelace lovelace => lovelace.Value,
            LovelaceWithMultiAsset lovelaceWithMultiAsset => lovelaceWithMultiAsset.Lovelace.Value,
            _ => null
        };

    public static string GetSubject(this MultiAssetOutput multiAssetOutput)
    {
        return multiAssetOutput.Value
            .Select(v => v.Value.Value
                .Select(tokenBundle =>
                    Convert.ToHexString(v.Key.Value) + Convert.ToHexString(tokenBundle.Key.Value))
                .First())
            .First();
    }
}