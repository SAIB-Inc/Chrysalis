using Chrysalis.Codec.Types.Cardano.Core.Transaction;

namespace Chrysalis.Codec.Extensions.Cardano.Core.Certificates;

/// <summary>
/// Extension methods for <see cref="Anchor"/> to access URL and data hash.
/// </summary>
public static class AnchorExtensions
{
    /// <summary>
    /// Gets the anchor URL as a <see cref="Uri"/>.
    /// </summary>
    /// <param name="self">The anchor instance.</param>
    /// <returns>The anchor URL.</returns>
    public static Uri Url(this Anchor self)
    {
        return new Uri(self.Url);
    }

    /// <summary>
    /// Gets the anchor data hash bytes.
    /// </summary>
    /// <param name="self">The anchor instance.</param>
    /// <returns>The data hash bytes.</returns>
    public static ReadOnlyMemory<byte> DataHash(this Anchor self)
    {
        return self.ContentHash;
    }
}
