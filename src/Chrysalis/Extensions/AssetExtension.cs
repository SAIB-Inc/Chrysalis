using Chrysalis.Cardano.Core;

namespace Chrysalis.Extensions;

public static class AssetExtension
{
    public static MultiAssetOutput? MultiAsset(this Value value)
        => value switch
        {
            LovelaceWithMultiAsset lovelaceWithMultiAsset => lovelaceWithMultiAsset.MultiAsset,
            _ => null
        };

    public static ulong? Coin(this Value value)
        => value switch
        {
            Lovelace lovelace => lovelace.Value,
            LovelaceWithMultiAsset lovelaceWithMultiAsset => lovelaceWithMultiAsset.Lovelace.Value,
            _ => null
        };

    public static LovelaceWithMultiAsset TransactionValueLovelace(this Value value)
        => value switch
        {
            LovelaceWithMultiAsset lovelaceWithMultiAsset => lovelaceWithMultiAsset,
            _ => throw new NotImplementedException()
        };

    public static IEnumerable<byte[]>? PolicyId(this MultiAsset multiAsset)
        => multiAsset switch
        {
            MultiAssetOutput x => x.Value.Keys.Select(key => key.Value),
            MultiAssetMint x => x.Value.Keys.Select(key => key.Value),
            _ => null
        };
    
    public static string Subject(this MultiAssetOutput multiAssetOutput)
    {
        return multiAssetOutput.Value
            .Select(v => v.Value.Value
                .Select(tokenBundle =>
                    Convert.ToHexString(v.Key.Value) + Convert.ToHexString(tokenBundle.Key.Value))
                .First())
            .First();
    }
    
    public static IEnumerable<string> Subjects(this MultiAssetOutput multiAssetOutput)
    {
        return multiAssetOutput.Value
            .SelectMany(v => v.Value.Value
                .Select(tokenBundle 
                    => Convert.ToHexString(tokenBundle.Key.Value)));
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
}