
using Chrysalis.Cardano.Core.Types.Block.Transaction.Output;

namespace Chrysalis.Cardano.Core.Extensions;

public static class ValueExtension
{
    public static ulong? Lovelace(this Value value)
        => value switch
        {
            Lovelace lovelace => lovelace.Value,
            LovelaceWithMultiAsset lovelaceWithMultiAsset => lovelaceWithMultiAsset.Lovelace.Value,
            _ => null
        };

    public static ulong? QuantityOf(this Value value, string policyId, string assetName) =>
        value.MultiAsset() is { } multiAsset
        && multiAsset.TryGetValue(policyId.ToLowerInvariant(), out var multiAssetOutput)
        && multiAssetOutput.TokenBundle().TryGetValue(assetName.ToLowerInvariant(), out var quantity)
            ? quantity
            : null;

    public static Dictionary<string, TokenBundleOutput>? MultiAsset(this Value value)
        => value switch
        {
            LovelaceWithMultiAsset lovelaceWithMultiAsset => lovelaceWithMultiAsset.MultiAsset.Value
                .ToDictionary(kvp => Convert.ToHexString(kvp.Key.Value).ToLowerInvariant(), kvp => kvp.Value),
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
}