using Chrysalis.Cbor.Serialization;
using Chrysalis.Cbor.Types.Cardano.Core.Transaction;

namespace Chrysalis.Cbor.Types.Cardano.Core.Byron;

/// <summary>
/// Adapter that wraps a <see cref="ByronTxOut"/> as a <see cref="TransactionOutput"/>,
/// enabling Byron outputs to flow through the era-agnostic API.
/// This type is NOT CBOR-serializable and is only constructed programmatically.
/// </summary>
public sealed record ByronTransactionOutputAdapter : TransactionOutput
{
    /// <summary>The underlying Byron transaction output.</summary>
    public ByronTxOut ByronTxOut { get; }

    /// <summary>
    /// Creates a new adapter wrapping the given Byron transaction output.
    /// </summary>
    /// <param name="byronTxOut">The Byron transaction output to wrap.</param>
    public ByronTransactionOutputAdapter(ByronTxOut byronTxOut)
    {
        ArgumentNullException.ThrowIfNull(byronTxOut);
        ByronTxOut = byronTxOut;
        Raw = CborSerializer.SerializeToMemory(byronTxOut);
    }
}
