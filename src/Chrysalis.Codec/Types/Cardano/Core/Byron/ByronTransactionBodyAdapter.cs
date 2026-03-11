using Chrysalis.Codec.Types.Cardano.Core.Transaction;

namespace Chrysalis.Codec.Types.Cardano.Core.Byron;

/// <summary>
/// Adapter that wraps a <see cref="ByronTx"/> as a <see cref="ITransactionBody"/>,
/// enabling Byron transactions to flow through the era-agnostic API.
/// This type is NOT CBOR-serializable and is only constructed programmatically.
/// </summary>
public sealed class ByronTransactionBodyAdapter : ITransactionBody
{
    /// <summary>The underlying Byron transaction.</summary>
    public ByronTx ByronTx { get; }

    /// <summary>The Byron transaction payload (transaction + witnesses).</summary>
    public ByronTxPayload TxPayload { get; }

    public ReadOnlyMemory<byte> Raw { get; }
    public int ConstrIndex => 0;
    public bool IsIndefinite => false;

    /// <summary>
    /// Creates a new adapter wrapping the given Byron transaction payload.
    /// </summary>
    /// <param name="txPayload">The Byron transaction payload to wrap.</param>
    public ByronTransactionBodyAdapter(ByronTxPayload txPayload)
    {
        TxPayload = txPayload;
        ByronTx = txPayload.Transaction;
        Raw = ByronTx.Raw;
    }
}
