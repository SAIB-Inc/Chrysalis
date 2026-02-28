using Chrysalis.Cbor.Extensions.Cardano.Core.Common;
using Chrysalis.Cbor.Extensions.Cardano.Core.Transaction;
using Chrysalis.Cbor.Types.Cardano.Core.Common;
using Chrysalis.Tx.Extensions;
using Chrysalis.Tx.Models.Cbor;

namespace Chrysalis.Tx.Utils;

/// <summary>
/// Implements coin selection algorithms for Cardano transaction building.
/// </summary>
public static class CoinSelectionUtil
{
    /// <summary>
    /// Selects UTxOs using the largest-first algorithm to satisfy the requested amounts.
    /// </summary>
    /// <param name="availableUtxos">The available UTxOs to select from.</param>
    /// <param name="requestedAmount">The required output values.</param>
    /// <param name="maxInputs">Maximum number of inputs to select.</param>
    /// <returns>A coin selection result with selected inputs and change.</returns>
    public static CoinSelectionResult LargestFirstAlgorithm(
    List<ResolvedInput> availableUtxos,
    List<Value> requestedAmount,
    int maxInputs = int.MaxValue
    )
    {
        if (availableUtxos == null || availableUtxos.Count == 0)
        {
            throw new InvalidOperationException("UTxO Balance Insufficient");
        }

        if (requestedAmount == null || requestedAmount.Count <= 0)
        {
            throw new ArgumentException("Requested amount must be greater than zero", nameof(requestedAmount));
        }

        ulong requestedLovelace = 0;
        Dictionary<string, ulong> requiredAssets = [];
        bool isLovelaceOnlyRequest = true;

        foreach (Value value in requestedAmount)
        {
            requestedLovelace += value.Lovelace();

            if (value is LovelaceWithMultiAsset lovelaceWithMultiAsset)
            {
                ExtractAssets(lovelaceWithMultiAsset.MultiAsset, requiredAssets);

                if (requiredAssets.Count > 0)
                {
                    isLovelaceOnlyRequest = false;
                }
            }
        }

        List<ResolvedInput> selectedUtxos = [];

        List<UtxoInfo> utxoInfo = [.. availableUtxos.Select(utxo =>
        {
            bool isLovelaceOnly = utxo.Output.Amount() is not LovelaceWithMultiAsset;

            int assetsCovered = 0;
            Dictionary<string, ulong> utxoAssets = [];

            if (!isLovelaceOnly)
            {
                LovelaceWithMultiAsset lovelaceWithMultiAsset = (LovelaceWithMultiAsset)utxo.Output.Amount();
                ExtractAssets(lovelaceWithMultiAsset.MultiAsset, utxoAssets);

                foreach (KeyValuePair<string, ulong> requiredAsset in requiredAssets)
                {
                    if (utxoAssets.TryGetValue(requiredAsset.Key, out ulong assetAmount) &&
                        assetAmount > 0)
                    {
                        assetsCovered++;
                    }
                }
            }

            return new UtxoInfo(utxo, utxo.Output.Amount().Lovelace(), isLovelaceOnly, assetsCovered, utxoAssets);
        })];

        if (isLovelaceOnlyRequest)
        {
            utxoInfo.Sort((a, b) =>
            {
                return a.IsLovelaceOnly != b.IsLovelaceOnly ? (a.IsLovelaceOnly ? -1 : 1) : b.Lovelace.CompareTo(a.Lovelace);
            });
        }
        else
        {
            utxoInfo.Sort((a, b) =>
            {
                int assetCompare = b.AssetsCovered.CompareTo(a.AssetsCovered);
                return assetCompare != 0 ? assetCompare : b.Lovelace.CompareTo(a.Lovelace);
            });
        }

        ulong selectedLovelace = 0;
        int selectedCount = 0;
        foreach (UtxoInfo info in utxoInfo)
        {
            if (selectedCount >= maxInputs)
            {
                break;
            }

            if (selectedLovelace >= requestedLovelace && requiredAssets.Count == 0)
            {
                break;
            }

            if (info.AssetsCovered == 0 && selectedLovelace >= requestedLovelace)
            {
                continue;
            }

            selectedUtxos.Add(info.Utxo);
            selectedLovelace += info.Lovelace;

            if (info.AssetsCovered > 0)
            {
                foreach (KeyValuePair<string, ulong> asset in info.Assets)
                {
                    if (requiredAssets.TryGetValue(asset.Key, out ulong requiredAmount2))
                    {
                        requiredAssets[asset.Key] = requiredAmount2 >= asset.Value ? requiredAmount2 - asset.Value : 0;
                        if (requiredAssets[asset.Key] == 0)
                        {
                            _ = requiredAssets.Remove(asset.Key);
                        }
                    }
                }
            }
            selectedCount++;
        }

        if (selectedLovelace < requestedLovelace)
        {
            throw new InvalidOperationException($"UTxO Balance Insufficient - need {requestedLovelace} lovelace but only found {selectedLovelace}");
        }

        if (requiredAssets.Count > 0)
        {
            throw new InvalidOperationException($"UTxO Balance Insufficient - missing assets: {string.Join(", ", requiredAssets.Keys)}");
        }

        ulong lovelaceChange = selectedLovelace - requestedLovelace;
        Dictionary<byte[], TokenBundleOutput> assetsChange = CalculateAssetsChange(selectedUtxos, requestedAmount);

        return new CoinSelectionResult
        {
            Inputs = selectedUtxos,
            LovelaceChange = lovelaceChange,
            AssetsChange = assetsChange
        };
    }

    private static void ExtractAssets(
        MultiAssetOutput multiAsset,
        Dictionary<string, ulong> assetDict)
    {
        if (multiAsset == null || multiAsset.Value == null)
        {
            return;
        }

        List<string> policies = multiAsset.PolicyId()?.ToList() ?? [];

        foreach (string policy in policies)
        {
            Dictionary<string, ulong> tokenBundle = multiAsset.TokenBundleByPolicyId(policy) ?? [];

            foreach (KeyValuePair<string, ulong> token in tokenBundle)
            {
                string assetKey = (policy + token.Key).ToUpperInvariant();

                assetDict[assetKey] = !assetDict.TryGetValue(assetKey, out ulong existingAmount)
                    ? token.Value
                    : existingAmount + token.Value;
            }
        }
    }

    private static Dictionary<byte[], TokenBundleOutput> CalculateAssetsChange(
    List<ResolvedInput> selectedUtxos,
    List<Value> requestedAmounts)
    {
        Dictionary<byte[], Dictionary<byte[], ulong>> selectedAssetsByPolicy = new(ByteArrayEqualityComparer.Instance);
        Dictionary<byte[], Dictionary<byte[], ulong>> requestedAssetsByPolicy = new(ByteArrayEqualityComparer.Instance);

        // Process selected UTXOs
        foreach (ResolvedInput utxo in selectedUtxos)
        {
            if (utxo.Output.Amount() is LovelaceWithMultiAsset lovelaceWithMultiAsset)
            {
                MultiAssetOutput multiAsset = lovelaceWithMultiAsset.MultiAsset;
                if (multiAsset?.Value == null)
                {
                    continue;
                }

                foreach (KeyValuePair<byte[], TokenBundleOutput> policyEntry in multiAsset.Value)
                {
                    if (!selectedAssetsByPolicy.TryGetValue(policyEntry.Key, out Dictionary<byte[], ulong>? selectedAssets))
                    {
                        selectedAssets = new Dictionary<byte[], ulong>(ByteArrayEqualityComparer.Instance);
                        selectedAssetsByPolicy[policyEntry.Key] = selectedAssets;
                    }

                    foreach (KeyValuePair<byte[], ulong> assetEntry in policyEntry.Value.Value)
                    {
                        ulong amount = assetEntry.Value;
                        selectedAssets[assetEntry.Key] = selectedAssets.TryGetValue(assetEntry.Key, out ulong existing) ? existing + amount : amount;
                    }
                }
            }
        }

        // Process requested amounts
        foreach (Value value in requestedAmounts)
        {
            if (value is LovelaceWithMultiAsset lovelaceWithMultiAsset)
            {
                MultiAssetOutput multiAsset = lovelaceWithMultiAsset.MultiAsset;
                if (multiAsset?.Value == null)
                {
                    continue;
                }

                foreach (KeyValuePair<byte[], TokenBundleOutput> policyEntry in multiAsset.Value)
                {
                    if (!requestedAssetsByPolicy.TryGetValue(policyEntry.Key, out Dictionary<byte[], ulong>? requestedAssets))
                    {
                        requestedAssets = new Dictionary<byte[], ulong>(ByteArrayEqualityComparer.Instance);
                        requestedAssetsByPolicy[policyEntry.Key] = requestedAssets;
                    }

                    foreach (KeyValuePair<byte[], ulong> assetEntry in policyEntry.Value.Value)
                    {
                        ulong amount = assetEntry.Value;
                        requestedAssets[assetEntry.Key] = requestedAssets.TryGetValue(assetEntry.Key, out ulong existing) ? existing + amount : amount;
                    }
                }
            }
        }

        // Calculate change using optimized lookups
        Dictionary<byte[], TokenBundleOutput> assetsChange = new(ByteArrayEqualityComparer.Instance);

        foreach ((byte[]? policyId, Dictionary<byte[], ulong>? selectedAssets) in selectedAssetsByPolicy)
        {
            Dictionary<byte[], ulong> assetChanges = new(ByteArrayEqualityComparer.Instance);
            bool hasChange = false;

            foreach ((byte[]? assetName, ulong selectedAmount) in selectedAssets)
            {
                ulong requestedAmount = 0;

                if (requestedAssetsByPolicy.TryGetValue(policyId, out Dictionary<byte[], ulong>? requestedTokens) &&
                    requestedTokens.TryGetValue(assetName, out ulong requested))
                {
                    requestedAmount = requested;
                }

                if (selectedAmount > requestedAmount)
                {
                    ulong change = selectedAmount - requestedAmount;
                    assetChanges[assetName] = change;
                    hasChange = true;
                }
            }

            if (hasChange)
            {
                assetsChange[policyId] = new TokenBundleOutput(assetChanges);
            }
        }

        return assetsChange;
    }

    private sealed record UtxoInfo(ResolvedInput Utxo, ulong Lovelace, bool IsLovelaceOnly, int AssetsCovered, Dictionary<string, ulong> Assets);
}

/// <summary>
/// Represents the result of a coin selection algorithm.
/// </summary>
public record CoinSelectionResult
{
    /// <summary>
    /// Gets or sets the selected input UTxOs.
    /// </summary>
    public List<ResolvedInput> Inputs { get; init; } = [];

    /// <summary>
    /// Gets or sets the lovelace change amount.
    /// </summary>
    public ulong LovelaceChange { get; set; }

    /// <summary>
    /// Gets or sets the multi-asset change amounts.
    /// </summary>
    public Dictionary<byte[], TokenBundleOutput> AssetsChange { get; init; } = [];
}
