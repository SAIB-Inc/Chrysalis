using Chrysalis.Cbor.Types.Cardano.Core.Common;
using Chrysalis.Cbor.Types.Cardano.Core.Transaction;

namespace Chrysalis.Tx.Extensions;

public static class TransactionOutputExtension
{
    public static ulong Lovelace(this TransactionOutput self)
    {
        return self switch
        {
            AlonzoTransactionOutput alonzoTransactionOutput => alonzoTransactionOutput.Amount switch
            {
                Lovelace lovelace => lovelace.Value,
                LovelaceWithMultiAsset lovelaceWithMultiAsset => lovelaceWithMultiAsset.LovelaceValue.Value,
                _ => throw new Exception("Unknown value type")
            },
            PostAlonzoTransactionOutput postAlonzoTransactionOutput => postAlonzoTransactionOutput.Amount switch
            {
                Lovelace lovelace => lovelace.Value,
                LovelaceWithMultiAsset lovelaceWithMultiAsset => lovelaceWithMultiAsset.LovelaceValue.Value,
                _ => throw new Exception("Unknown value type")
            },
            _ => throw new Exception("Unknown transaction output type")
        };
    }

    public static Value Amount(this TransactionOutput self)
    {
        return self switch
        {
            AlonzoTransactionOutput alonzoTransactionOutput => alonzoTransactionOutput.Amount,
            PostAlonzoTransactionOutput postAlonzoTransactionOutput => postAlonzoTransactionOutput.Amount,
            _ => throw new Exception("Unknown transaction output type")
        };
    }
}