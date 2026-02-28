using CMetadata = Chrysalis.Cbor.Types.Cardano.Core.Metadata;
using Chrysalis.Cbor.Types.Cardano.Core.Transaction;

namespace Chrysalis.Cbor.Extensions.Cardano.Core.Transaction;

/// <summary>
/// Extension methods for <see cref="CMetadata"/> to access metadata values.
/// </summary>
public static class MetadataExtensions
{
    /// <summary>
    /// Gets the metadata dictionary mapping labels to metadatums.
    /// </summary>
    /// <param name="self">The metadata instance.</param>
    /// <returns>The metadata dictionary.</returns>
    public static Dictionary<ulong, TransactionMetadatum> Value(this CMetadata self)
    {
        ArgumentNullException.ThrowIfNull(self);
        return self.Value;
    }
}
