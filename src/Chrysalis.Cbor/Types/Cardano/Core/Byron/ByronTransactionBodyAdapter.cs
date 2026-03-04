using Chrysalis.Cbor.Types.Cardano.Core.Transaction;

namespace Chrysalis.Cbor.Types.Cardano.Core.Byron;

/// <summary>
/// Adapter that wraps a <see cref="ByronTx"/> as a <see cref="TransactionBody"/>,
/// enabling Byron transactions to flow through the era-agnostic API.
/// This type is NOT CBOR-serializable and is only constructed programmatically.
/// </summary>
public sealed record ByronTransactionBodyAdapter : TransactionBody
{
    /// <summary>The underlying Byron transaction.</summary>
    public ByronTx ByronTx { get; }

    /// <summary>The Byron transaction payload (transaction + witnesses).</summary>
    public ByronTxPayload TxPayload { get; }

    /// <summary>
    /// Creates a new adapter wrapping the given Byron transaction payload.
    /// </summary>
    /// <param name="txPayload">The Byron transaction payload to wrap.</param>
    public ByronTransactionBodyAdapter(ByronTxPayload txPayload)
    {
        ArgumentNullException.ThrowIfNull(txPayload);
        TxPayload = txPayload;
        ByronTx = txPayload.Transaction;
        Raw = ByronTx.Raw;
    }
}
