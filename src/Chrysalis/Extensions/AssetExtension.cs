using Chrysalis.Cardano.Cbor;
using Chrysalis.Cardano.Core;

namespace Chrysalis.Extensions;

public static class AssetExtension
{
    public static ulong? Coin(this Value value)
        => value switch
        {
            Lovelace lovelace => lovelace.Value,
            LovelaceWithMultiAsset lovelaceWithMultiAsset => lovelaceWithMultiAsset.Lovelace.Value,
            _ => null
        };

    public static Dictionary<string, TokenBundleOutput>? MultiAsset(this Value value)
        => value switch
        {
            LovelaceWithMultiAsset lovelaceWithMultiAsset => lovelaceWithMultiAsset.MultiAsset.Value
                .ToDictionary(kvp => Convert.ToHexString(kvp.Key.Value), kvp => kvp.Value),
            _ => null
        };
    
    public static MultiAssetOutput? MultiAssetOutput(this Value value)
        => value switch
        {
            LovelaceWithMultiAsset lovelaceWithMultiAsset => lovelaceWithMultiAsset.MultiAsset,
            _ => null
        };

    public static bool WithMultiAsset(this Value value)
        => value is LovelaceWithMultiAsset;

    public static IEnumerable<string>? PolicyId(this MultiAssetOutput multiAssetOutput)
        => multiAssetOutput.Value.Keys.Select(key => Convert.ToHexString(key.Value));

    public static IEnumerable<string>? PolicyId(this MultiAssetMint multiAssetMint)
        => multiAssetMint.Value.Keys.Select(key => Convert.ToHexString(key.Value));
    
    public static IEnumerable<string> AssetNames(this MultiAssetOutput multiAssetOutput)
    {
        return multiAssetOutput.Value
            .SelectMany(v => v.Value.Value
                .Select(tokenBundle 
                    => Convert.ToHexString(tokenBundle.Key.Value)));
    }

    public static IEnumerable<string> AssetNames(this MultiAssetMint multiAssetMint)
    {
        return multiAssetMint.Value
            .SelectMany(v => v.Value.Value
                .Select(tokenBundle 
                    => Convert.ToHexString(tokenBundle.Key.Value)));
    }

    public static IEnumerable<string> Subjects(this MultiAssetOutput multiAssetOutput)
    {
        return multiAssetOutput.Value
            .SelectMany(v => v.Value.Value
                .Select(tokenBundle 
                    => Convert.ToHexString(v.Key.Value) + Convert.ToHexString(tokenBundle.Key.Value)));
    }
    
    public static IEnumerable<string> Subjects(this MultiAssetMint multiAssetMint)
    {
        return multiAssetMint.Value
            .SelectMany(v => v.Value.Value
                .Select(tokenBundle 
                    => Convert.ToHexString(v.Key.Value) +Convert.ToHexString(tokenBundle.Key.Value)));
    }

    public static Dictionary<string, ulong> TokenBundle(this MultiAssetOutput multiAssetOutput)
    {
        return multiAssetOutput.Value
            .SelectMany(v => v.Value.Value
                .Select(tokenBundle =>
                    new KeyValuePair<string, ulong>(
                        Convert.ToHexString(tokenBundle.Key.Value),
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
                        Convert.ToHexString(tokenBundle.Key.Value),
                        tokenBundle.Value.Value
                    )))
            .ToDictionary(kvp => kvp.Key, kvp => kvp.Value);
    }

    public static Dictionary<string, ulong> TokenBundle(this TokenBundleOutput tokenBundleOutput)
    {
        return tokenBundleOutput.Value
            .ToDictionary(kvp => Convert.ToHexString(kvp.Key.Value), kvp => kvp.Value.Value);
    }

    public static Dictionary<string, long> TokenBundle(this TokenBundleMint tokenBundleMint)
    {
        return tokenBundleMint.Value
            .ToDictionary(kvp => Convert.ToHexString(kvp.Key.Value), kvp => kvp.Value.Value);
    }
}