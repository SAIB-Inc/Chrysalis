using Chrysalis.Cbor.Types.Cardano.Core.Transaction;

namespace Chrysalis.Cbor.Extensions.Cardano.Core.Certificates;

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
        ArgumentNullException.ThrowIfNull(self);
        return new Uri(self.AnchorUrl);
    }

    /// <summary>
    /// Gets the anchor data hash bytes.
    /// </summary>
    /// <param name="self">The anchor instance.</param>
    /// <returns>The data hash bytes.</returns>
    public static byte[] DataHash(this Anchor self)
    {
        ArgumentNullException.ThrowIfNull(self);
        return self.AnchorDataHash;
    }
}
