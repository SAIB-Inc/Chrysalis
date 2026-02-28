using Chrysalis.Cbor.Extensions.Cardano.Core.Transaction;
using Chrysalis.Cbor.Types.Cardano.Core.Common;

namespace Chrysalis.Cbor.Extensions.Cardano.Core.Common;

/// <summary>
/// Extension methods for <see cref="Value"/> to access lovelace and multi-asset amounts.
/// </summary>
public static class ValueExtensions
{
    /// <summary>
    /// Gets the lovelace amount from the value.
    /// </summary>
    /// <param name="self">The value instance.</param>
    /// <returns>The lovelace amount.</returns>
    public static ulong Lovelace(this Value self)
    {
        ArgumentNullException.ThrowIfNull(self);
        return self switch
        {
            Lovelace lovelace => lovelace.Value,
            LovelaceWithMultiAsset lovelaceWithMultiAsset => lovelaceWithMultiAsset.LovelaceValue.Value,
            _ => default
        };
    }

    /// <summary>
    /// Gets the multi-asset dictionary from the value.
    /// </summary>
    /// <param name="self">The value instance.</param>
    /// <returns>The multi-asset dictionary mapping hex-encoded policy IDs to token bundles.</returns>
    public static Dictionary<string, TokenBundleOutput> MultiAsset(this Value self)
    {
        ArgumentNullException.ThrowIfNull(self);
        return self switch
        {
            LovelaceWithMultiAsset lovelaceWithMultiAsset => lovelaceWithMultiAsset.MultiAsset.Value
                .ToDictionary(
                    kvp => Convert.ToHexString(kvp.Key.Span).ToUpperInvariant(),
                    kvp => kvp.Value
                ),
            _ => []
        };
    }

    /// <summary>
    /// Gets the quantity of a specific asset identified by its subject (policy ID + asset name).
    /// </summary>
    /// <param name="self">The value instance.</param>
    /// <param name="subject">The subject string (hex-encoded policy ID concatenated with hex-encoded asset name).</param>
    /// <returns>The quantity, or null if the asset is not present or the value has no multi-assets.</returns>
    public static ulong? QuantityOf(this Value self, string subject)
    {
        ArgumentNullException.ThrowIfNull(self);
        ArgumentNullException.ThrowIfNull(subject);

        if (self is LovelaceWithMultiAsset multiAsset)
        {
            ulong amount = multiAsset.MultiAsset.ToDict()
                .SelectMany(ma =>
                    ma.Value.ToDict()
                    .Where(tb =>
                    {
                        string policyId = ma.Key;
                        string assetName = tb.Key;
                        string fullSubject = string.Concat(policyId, assetName);

                        return string.Equals(fullSubject, subject, StringComparison.OrdinalIgnoreCase);
                    })
                    .Select(tb => tb.Value)
                )
                .FirstOrDefault();

            return amount;
        }

        return null;
    }
}
