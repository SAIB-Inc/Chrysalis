using Chrysalis.Cbor.Types.Cardano.Core.Common;

namespace Chrysalis.Cbor.Extensions.Cardano.Core.Common;

/// <summary>
/// Extension methods for <see cref="TokenBundleMint"/> to access the mint values.
/// </summary>
public static class TokenBundleMintExtensions
{
    /// <summary>
    /// Gets the mint dictionary mapping asset names to signed quantities.
    /// </summary>
    /// <param name="self">The token bundle mint instance.</param>
    /// <returns>The mint dictionary.</returns>
    public static Dictionary<byte[], long> Value(this TokenBundleMint self)
    {
        ArgumentNullException.ThrowIfNull(self);
        return self.Value;
    }
}
