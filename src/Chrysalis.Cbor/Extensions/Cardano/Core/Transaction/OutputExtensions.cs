using Chrysalis.Cbor.Types.Cardano.Core.Common;
using Chrysalis.Cbor.Types.Cardano.Core.Transaction;

namespace Chrysalis.Cbor.Extensions.Cardano.Core.Transaction;

/// <summary>
/// Extension methods for <see cref="TransactionOutput"/> to access output fields across eras.
/// </summary>
public static class OutputExtensions
{
    /// <summary>
    /// Gets the address bytes from the transaction output.
    /// </summary>
    /// <param name="self">The transaction output instance.</param>
    /// <returns>The address bytes.</returns>
    public static byte[] Address(this TransactionOutput self)
    {
        ArgumentNullException.ThrowIfNull(self);
        return self switch
        {
            AlonzoTransactionOutput alonzoTxOutput => alonzoTxOutput.Address.Value,
            PostAlonzoTransactionOutput postAlonzoTxOutput => postAlonzoTxOutput.Address.Value,
            _ => []
        };
    }

    /// <summary>
    /// Gets the value from the transaction output.
    /// </summary>
    /// <param name="self">The transaction output instance.</param>
    /// <returns>The output value.</returns>
    public static Value Amount(this TransactionOutput self)
    {
        ArgumentNullException.ThrowIfNull(self);
        return self switch
        {
            AlonzoTransactionOutput alonzoTxOutput => alonzoTxOutput.Amount,
            PostAlonzoTransactionOutput postAlonzoTxOutput => postAlonzoTxOutput.Amount,
            _ => throw new NotImplementedException()
        };
    }

    /// <summary>
    /// Gets the datum hash from the transaction output, if present.
    /// </summary>
    /// <param name="self">The transaction output instance.</param>
    /// <returns>The datum hash bytes, or null.</returns>
    public static byte[]? DatumHash(this TransactionOutput self)
    {
        ArgumentNullException.ThrowIfNull(self);
        return self switch
        {
            AlonzoTransactionOutput alonzoTxOutput => alonzoTxOutput.DatumHash,
            PostAlonzoTransactionOutput postAlonzoTxOutput => postAlonzoTxOutput.Datum switch
            {
                DatumHashOption datumHashOption => datumHashOption.DatumHash,
                _ => null
            },
            _ => null
        };
    }

    /// <summary>
    /// Gets the datum option from the transaction output, if present.
    /// </summary>
    /// <param name="self">The transaction output instance.</param>
    /// <returns>The datum option, or null.</returns>
    public static DatumOption? DatumOption(this TransactionOutput self)
    {
        ArgumentNullException.ThrowIfNull(self);
        return self switch
        {
            PostAlonzoTransactionOutput postAlonzoTxOutput => postAlonzoTxOutput.Datum,
            _ => null
        };
    }

    /// <summary>
    /// Gets the script reference bytes from the transaction output, if present.
    /// </summary>
    /// <param name="self">The transaction output instance.</param>
    /// <returns>The script reference bytes, or null.</returns>
    public static byte[]? ScriptRef(this TransactionOutput self)
    {
        ArgumentNullException.ThrowIfNull(self);
        return self switch
        {
            PostAlonzoTransactionOutput postAlonzoTxOutput => postAlonzoTxOutput.ScriptRef?.Value,
            _ => null
        };
    }
}
