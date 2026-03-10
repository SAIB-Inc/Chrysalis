using Chrysalis.Codec.V2.Types.Cardano.Core.Transaction;

namespace Chrysalis.Codec.V2.Extensions.Cardano.Core.Transaction;

/// <summary>
/// Extension methods for <see cref="ITransactionMetadatum"/> to access raw bytes.
/// </summary>
public static class TransactionMetadatumExtensions
{
    /// <summary>
    /// Gets the raw CBOR bytes of the transaction metadatum.
    /// </summary>
    /// <param name="self">The transaction metadatum instance.</param>
    /// <returns>The raw bytes, or empty if not available.</returns>
    public static ReadOnlyMemory<byte> Raw(this ITransactionMetadatum self)
    {
        ArgumentNullException.ThrowIfNull(self);
        return self.Raw;
    }
}
