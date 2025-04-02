using Chrysalis.Cbor.Types.Cardano.Core.Common;

namespace Chrysalis.Tx.Extensions;

public static class MultiAssetExtension
{
    public static IEnumerable<string> PolicyId(this MultiAsset self)
    {
        IEnumerable<string> policies = self switch
        {
            MultiAssetOutput multiAssetOutput => multiAssetOutput.Value.Keys.Select(Convert.ToHexString),
            MultiAssetMint multiAssetMint => multiAssetMint.Value.Keys.Select(Convert.ToHexString),
            _ => throw new Exception("Unknown multi asset type")
        };

        return policies;
    }


    public static Dictionary<string, ulong>? TokenBundleByPolicyId(this MultiAsset self, string policyId)
    {
        try
        {
            var policyIdBytes = Convert.FromHexString(policyId);

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
        catch
        {
            return null;
        }
    }
    private static Dictionary<string, ulong>? FindTokenBundle<TBundle>(
        Dictionary<byte[], TBundle> bundles,
        byte[] policyId,
        Func<TBundle, Dictionary<string, ulong>> converter)
    {
        foreach (var kvp in bundles)
        {
            if (kvp.Key.SequenceEqual(policyId))
            {
                return converter(kvp.Value);
            }
        }

        return null;
    }
}