using Chrysalis.Cbor.Types.Cardano.Core.Common;

namespace Chrysalis.Cbor.Extensions.Cardano.Core.Transaction;

/// <summary>
/// Extension methods for <see cref="MultiAssetOutput"/> to convert to a dictionary.
/// </summary>
public static class MultiAssetOutputExtensions
{
    /// <summary>
    /// Converts the multi-asset output to a dictionary of policy ID to token bundle.
    /// </summary>
    /// <param name="self">The multi-asset output instance.</param>
    /// <returns>The dictionary mapping policy IDs to token bundles.</returns>
    public static Dictionary<byte[], TokenBundleOutput> ToDict(this MultiAssetOutput self)
    {
        ArgumentNullException.ThrowIfNull(self);
        return self.Value;
    }
}
