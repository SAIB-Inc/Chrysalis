using Chrysalis.Cbor.Types.Cardano.Core.Common;

namespace Chrysalis.Tx.Extensions;

/// <summary>
/// Extension methods for working with Cardano multi-asset values.
/// </summary>
public static class MultiAssetExtension
{
    /// <summary>
    /// Extracts the policy IDs from a multi-asset value as hex strings.
    /// </summary>
    /// <param name="self">The multi-asset value.</param>
    /// <returns>An enumerable of policy ID hex strings.</returns>
    public static IEnumerable<string> PolicyId(this MultiAsset self)
    {
        ArgumentNullException.ThrowIfNull(self);

        IEnumerable<string> policies = self switch
        {
            MultiAssetOutput multiAssetOutput => multiAssetOutput.Value.Keys.Select(Convert.ToHexString),
            MultiAssetMint multiAssetMint => multiAssetMint.Value.Keys.Select(Convert.ToHexString),
            _ => throw new InvalidOperationException("Unknown multi asset type")
        };

        return policies;
    }


    /// <summary>
    /// Gets the token bundle for a specific policy ID as a dictionary of asset name hex to quantity.
    /// </summary>
    /// <param name="self">The multi-asset value.</param>
    /// <param name="policyId">The policy ID hex string.</param>
    /// <returns>The token bundle dictionary, or null if not found.</returns>
    public static Dictionary<string, ulong>? TokenBundleByPolicyId(this MultiAsset self, string policyId)
    {
        ArgumentNullException.ThrowIfNull(self);
        ArgumentNullException.ThrowIfNull(policyId);

        try
        {
            byte[] policyIdBytes = Convert.FromHexString(policyId);

            return self switch
            {
                MultiAssetOutput multiAssetOutput => FindTokenBundle(
                    multiAssetOutput.Value,
                    policyIdBytes,
                    bundle => bundle.Value.ToDictionary(
                        x => Convert.ToHexString(x.Key),
                        x => x.Value)
                ),

                MultiAssetMint multiAssetMint => FindTokenBundle(
                    multiAssetMint.Value,
                    policyIdBytes,
                    bundle => bundle.Value.ToDictionary(
                        x => Convert.ToHexString(x.Key),
                        x => (ulong)x.Value)
                ),

                _ => null
            };
        }
        catch (FormatException)
        {
            return null;
        }
    }

    private static Dictionary<string, ulong>? FindTokenBundle<TBundle>(
        Dictionary<byte[], TBundle> bundles,
        byte[] policyId,
        Func<TBundle, Dictionary<string, ulong>> converter)
    {
        foreach (KeyValuePair<byte[], TBundle> kvp in bundles)
        {
            if (kvp.Key.SequenceEqual(policyId))
            {
                return converter(kvp.Value);
            }
        }

        return null;
    }
}
