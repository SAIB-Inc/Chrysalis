using Chrysalis.Codec.Types.Cardano.Core.Common;

namespace Chrysalis.Codec.Extensions.Cardano.Core.Common;

/// <summary>
/// Extension methods for <see cref="TokenBundleMint"/> to access the mint values.
/// </summary>
public static class TokenBundleMintExtensions
{
    /// <summary>
    /// Gets the mint dictionary mapping hex-encoded asset names to signed quantities.
    /// </summary>
    /// <param name="self">The token bundle mint instance.</param>
    /// <returns>The mint dictionary.</returns>
    public static Dictionary<string, long> Value(this TokenBundleMint self)
    {
        return self.Value.ToDictionary(
            kvp => Convert.ToHexString(kvp.Key.Span).ToUpperInvariant(),
            kvp => kvp.Value
        );
    }
}
