using Chrysalis.Cbor.Types.Cardano.Core.Common;
using Chrysalis.Cbor.Types.Cardano.Core.Transaction;

namespace Chrysalis.Cbor.Extensions.Cardano.Core.Transaction.Body.Output;

public static class OutputExtensions
{
    public static byte[] Address(this TransactionOutput self) =>
        self switch
        {
            AlonzoTransactionOutput alonzoTxOutput => alonzoTxOutput.Address.Value,
            PostAlonzoTransactionOutput postAlonzoTxOutput => postAlonzoTxOutput.Address.Value,
            _ => []
        };

    public static Value Amount(this TransactionOutput self) =>
        self switch
        {
            AlonzoTransactionOutput alonzoTxOutput => alonzoTxOutput.Amount,
            PostAlonzoTransactionOutput postAlonzoTxOutput => postAlonzoTxOutput.Amount,
            _ => throw new NotImplementedException()
        };

    public static byte[]? DatumHash(this TransactionOutput self) =>
        self switch
        {
            AlonzoTransactionOutput alonzoTxOutput => alonzoTxOutput.DatumHash,
            PostAlonzoTransactionOutput postAlonzoTxOutput => postAlonzoTxOutput.Datum switch
            {
                DatumHashOption datumHashOption => datumHashOption.DatumHash,
                _ => null
            },
            _ => null
        };

    public static DatumOption? DatumOption(this TransactionOutput self) =>
        self switch
        {
            PostAlonzoTransactionOutput postAlonzoTxOutput => postAlonzoTxOutput.Datum,
            _ => null
        };

    public static byte[]? ScriptRef(this TransactionOutput self) =>
        self switch
        {
            PostAlonzoTransactionOutput postAlonzoTxOutput => postAlonzoTxOutput.ScriptRef?.Value,
            _ => null
        };
}