using CMetadata = Chrysalis.Codec.V2.Types.Cardano.Core.Metadata;
using Chrysalis.Codec.V2.Types.Cardano.Core.Transaction;

namespace Chrysalis.Codec.V2.Extensions.Cardano.Core.Transaction;

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
    public static Dictionary<ulong, ITransactionMetadatum> Value(this CMetadata self)
    {
        return self.Value;
    }
}
