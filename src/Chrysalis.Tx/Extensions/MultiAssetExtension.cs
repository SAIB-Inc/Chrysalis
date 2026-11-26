using Chrysalis.Codec.Extensions.Cardano.Core.Common;
using Chrysalis.Codec.Serialization;
using Chrysalis.Codec.Types;
using Chrysalis.Codec.Types.Cardano.Core.Common;

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
    public static IEnumerable<string> PolicyId(this IMultiAsset self)
    {
        ArgumentNullException.ThrowIfNull(self);

        return self switch
        {
            MultiAssetOutput multiAssetOutput => multiAssetOutput.Value.Select(kv => Convert.ToHexString(kv.Key.Span)),
            MultiAssetMint multiAssetMint => multiAssetMint.Value.Select(kv => Convert.ToHexString(kv.Key.Span)),
            _ => throw new InvalidOperationException("Unknown multi asset type")
        };
    }

    /// <summary>
    /// Gets the token bundle for a specific policy ID as a dictionary of asset name hex to quantity.
    /// </summary>
    /// <param name="self">The multi-asset value.</param>
    /// <param name="policyId">The policy ID hex string.</param>
    /// <returns>The token bundle dictionary, or null if not found.</returns>
    public static Dictionary<string, ulong>? TokenBundleByPolicyId(this IMultiAsset self, string policyId)
    {
        ArgumentNullException.ThrowIfNull(self);
        ArgumentNullException.ThrowIfNull(policyId);

        try
        {
            ReadOnlyMemory<byte> policyIdMemory = Convert.FromHexString(policyId);
            return self switch
            {
                MultiAssetOutput multiAssetOutput => FindTokenBundle(multiAssetOutput.Value, policyIdMemory, static bundle => bundle.ToDict()),
                MultiAssetMint multiAssetMint => FindTokenBundle(multiAssetMint.Value, policyIdMemory, static bundle =>
                {
                    CborMap<ReadOnlyMemory<byte>, long> entries = bundle.Value;
                    Dictionary<string, ulong> result = [with(Math.Clamp(entries.Count, 0, 256))];
                    foreach ((ReadOnlyMemory<byte> asset, long qty) in entries) { result[Convert.ToHexString(asset.Span)] = (ulong)qty; }
                    return result;
                }),
                _ => null
            };
        }
        catch (FormatException error) when (error is not CborDecodeException)
        {
            return null;
        }
    }

    private static Dictionary<string, ulong>? FindTokenBundle<TBundle>(
        CborMap<ReadOnlyMemory<byte>, TBundle> bundles,
        ReadOnlyMemory<byte> policyId,
        Func<TBundle, Dictionary<string, ulong>> converter) =>
        bundles.TryGetValue(policyId, out TBundle? bundle) ? converter(bundle) : null;
}
