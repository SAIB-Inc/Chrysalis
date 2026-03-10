using Chrysalis.Codec.Types.Cardano.Core.Common;

namespace Chrysalis.Codec.Extensions.Cardano.Core.Common;

/// <summary>
/// Extension methods for <see cref="TokenBundleOutput"/> to convert to a dictionary.
/// </summary>
public static class TokenBundleOutputExtensions
{
    /// <summary>
    /// Converts the token bundle output to a dictionary of hex-encoded asset name to quantity.
    /// </summary>
    /// <param name="self">The token bundle output instance.</param>
    /// <returns>The dictionary mapping hex-encoded asset names to quantities.</returns>
    public static Dictionary<string, ulong> ToDict(this TokenBundleOutput self)
    {
        return self.Value.ToDictionary(
            kvp => Convert.ToHexString(kvp.Key.Span).ToUpperInvariant(),
            kvp => kvp.Value
        );
    }
}
