using Chrysalis.Cbor.Types.Cardano.Core.Transaction;

namespace Chrysalis.Cbor.Extensions.Cardano.Core.Transaction;

/// <summary>
/// Extension methods for <see cref="TransactionMetadatum"/> to access raw bytes.
/// </summary>
public static class TransactionMetadatumExtensions
{
    /// <summary>
    /// Gets the raw CBOR bytes of the transaction metadatum.
    /// </summary>
    /// <param name="self">The transaction metadatum instance.</param>
    /// <returns>The raw bytes, or empty if not available.</returns>
    public static ReadOnlyMemory<byte> Raw(this TransactionMetadatum self)
    {
        ArgumentNullException.ThrowIfNull(self);
        return self.Raw ?? ReadOnlyMemory<byte>.Empty;
    }
}
