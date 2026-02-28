using Chrysalis.Cbor.Types.Cardano.Core.Common;

namespace Chrysalis.Cbor.Extensions.Cardano.Core.Common;

/// <summary>
/// Extension methods for <see cref="TokenBundleOutput"/> to convert to a dictionary.
/// </summary>
public static class TokenBundleOutputExtensions
{
    /// <summary>
    /// Converts the token bundle output to a dictionary of asset name to quantity.
    /// </summary>
    /// <param name="self">The token bundle output instance.</param>
    /// <returns>The dictionary mapping asset names to quantities.</returns>
    public static Dictionary<byte[], ulong> ToDict(this TokenBundleOutput self)
    {
        ArgumentNullException.ThrowIfNull(self);
        return self.Value;
    }
}
