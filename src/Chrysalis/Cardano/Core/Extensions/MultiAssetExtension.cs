using Chrysalis.Cardano.Core.Types.Block.Transaction.Output;

namespace Chrysalis.Cardano.Core.Extensions;

public static class MultiAssetExtension
{
    public static IEnumerable<string>? PolicyId(this MultiAssetOutput multiAssetOutput)
        => multiAssetOutput.Value.Keys.Select(key => Convert.ToHexString(key.Value).ToLowerInvariant());

    public static IEnumerable<string>? PolicyId(this MultiAssetMint multiAssetMint)
        => multiAssetMint.Value.Keys.Select(key => Convert.ToHexString(key.Value).ToLowerInvariant());

    public static IEnumerable<string> AssetNames(this MultiAssetOutput multiAssetOutput)
    {
        return multiAssetOutput.Value
            .SelectMany(v => v.Value.Value
                .Select(tokenBundle
                    => Convert.ToHexString(tokenBundle.Key.Value).ToLowerInvariant()));
    }

    public static IEnumerable<string> AssetNames(this MultiAssetMint multiAssetMint)
    {
        return multiAssetMint.Value
            .SelectMany(v => v.Value.Value
                .Select(tokenBundle
                    => Convert.ToHexString(tokenBundle.Key.Value).ToLowerInvariant()));
    }

    public static IEnumerable<string> Subjects(this MultiAssetOutput multiAssetOutput)
    {
        return multiAssetOutput.Value
            .SelectMany(v => v.Value.Value
                .Select(tokenBundle
                    => Convert.ToHexString(v.Key.Value).ToLowerInvariant() + Convert.ToHexString(tokenBundle.Key.Value).ToLowerInvariant()));
    }

    public static IEnumerable<(string, string)> SubjectTuples(this MultiAssetOutput multiAssetOutput)
    {
        return multiAssetOutput.Value
            .SelectMany(v => v.Value.Value
                .Select(tokenBundle
                    => (Convert.ToHexString(v.Key.Value).ToLowerInvariant(), Convert.ToHexString(tokenBundle.Key.Value).ToLowerInvariant())));
    }

    public static IEnumerable<string> Subjects(this MultiAssetMint multiAssetMint)
    {
        return multiAssetMint.Value
            .SelectMany(v => v.Value.Value
                .Select(tokenBundle
                    => Convert.ToHexString(v.Key.Value).ToLowerInvariant() + Convert.ToHexString(tokenBundle.Key.Value).ToLowerInvariant()));
    }

    public static Dictionary<string, ulong> TokenBundle(this MultiAssetOutput multiAssetOutput)
    {
        return multiAssetOutput.Value
            .SelectMany(v => v.Value.Value
                .Select(tokenBundle =>
                    new KeyValuePair<string, ulong>(
                        Convert.ToHexString(tokenBundle.Key.Value).ToLowerInvariant(),
                        tokenBundle.Value.Value
                    )))
            .ToDictionary(kvp => kvp.Key, kvp => kvp.Value);
    }

    public static Dictionary<string, long> TokenBundle(this MultiAssetMint multiAssetMint)
    {
        return multiAssetMint.Value
            .SelectMany(v => v.Value.Value
                .Select(tokenBundle =>
                    new KeyValuePair<string, long>(
                        Convert.ToHexString(tokenBundle.Key.Value).ToLowerInvariant(),
                        tokenBundle.Value.Value
                    )))
            .ToDictionary(kvp => kvp.Key, kvp => kvp.Value);
    }

    public static Dictionary<string, ulong>? TokenBundleByPolicyId(this MultiAssetOutput multiAssetOutput, string policyId)
    {
        return multiAssetOutput.Value
            .Where(v => Convert.ToHexString(v.Key.Value).ToLowerInvariant() == policyId.ToLowerInvariant())
            .SelectMany(v => v.Value.Value
                .Select(tokenBundle =>
                    new KeyValuePair<string, ulong>(
                        Convert.ToHexString(tokenBundle.Key.Value).ToLowerInvariant(),
                        tokenBundle.Value.Value
                    )))
            .ToDictionary(kvp => kvp.Key, kvp => kvp.Value);
    }
}