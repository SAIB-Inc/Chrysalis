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

    public static Dictionary<string, ulong> TokenBundleByPolicyId(this MultiAsset self, string policyId)
    {
        Dictionary<string, ulong> tokenBundle = self switch
        {
            MultiAssetOutput multiAssetOutput => multiAssetOutput.Value[Convert.FromHexString(policyId)].Value.Select(x => (Convert.ToHexString(x.Key), x.Value)).ToDictionary(x => x.Item1, x => x.Value),
            MultiAssetMint multiAssetMint => multiAssetMint.Value[Convert.FromHexString(policyId)].Value.Select(x => (Convert.ToHexString(x.Key), x.Value)).ToDictionary(x => x.Item1, x => (ulong)x.Value),
            _ => throw new Exception("Unknown multi asset type")
        };

        return tokenBundle;
    }
}