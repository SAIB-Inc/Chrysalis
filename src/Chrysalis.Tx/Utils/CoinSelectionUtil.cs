using Chrysalis.Cbor.Extensions.Cardano.Core.Common;
using Chrysalis.Cbor.Extensions.Cardano.Core.Transaction;
using Chrysalis.Cbor.Types.Cardano.Core.Common;
using Chrysalis.Tx.Extensions;
using Chrysalis.Tx.Models.Cbor;

namespace Chrysalis.Tx.Utils;

//@TODO: Unit Tests
public static class CoinSelectionUtil
{
    public static CoinSelectionResult LargestFirstAlgorithm(
    List<ResolvedInput> availableUtxos,
    List<Value> requestedAmount,
    int maxInputs = int.MaxValue
    )
    {
        if (availableUtxos == null || availableUtxos.Count == 0)
            throw new InvalidOperationException("UTxO Balance Insufficient");

        if (requestedAmount == null || requestedAmount.Count <= 0)
            throw new ArgumentException("Requested amount must be greater than zero", nameof(requestedAmount));

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

        var utxoInfo = availableUtxos.Select(utxo =>
        {
            bool isLovelaceOnly = !(utxo.Output.Amount() is LovelaceWithMultiAsset);

            int assetsCovered = 0;
            Dictionary<string, ulong> utxoAssets = [];

            if (!isLovelaceOnly)
            {
                var lovelaceWithMultiAsset = (LovelaceWithMultiAsset)utxo.Output.Amount();
                ExtractAssets(lovelaceWithMultiAsset.MultiAsset, utxoAssets);

                foreach (var requiredAsset in requiredAssets)
                {
                    if (utxoAssets.ContainsKey(requiredAsset.Key) &&
                        utxoAssets[requiredAsset.Key] > 0)
                    {
                        assetsCovered++;
                    }
                }
            }

            return new
            {
                Utxo = utxo,
                Lovelace = utxo.Output.Amount().Lovelace(),
                IsLovelaceOnly = isLovelaceOnly,
                AssetsCovered = assetsCovered,
                Assets = utxoAssets
            };
        }).ToList();

        if (isLovelaceOnlyRequest)
        {
            utxoInfo.Sort((a, b) =>
            {
                if (a.IsLovelaceOnly != b.IsLovelaceOnly)
                {
                    return a.IsLovelaceOnly ? -1 : 1;
                }

                return b.Lovelace.CompareTo(a.Lovelace);
            });
        }
        else
        {
            utxoInfo.Sort((a, b) =>
            {
                int assetCompare = b.AssetsCovered.CompareTo(a.AssetsCovered);
                if (assetCompare != 0) return assetCompare;

                return b.Lovelace.CompareTo(a.Lovelace);
            });
        }

        ulong selectedLovelace = 0;
        int selectedCount = 0;
        foreach (var info in utxoInfo)
        {
            if (selectedCount >= maxInputs)
                break;

            if (selectedLovelace >= requestedLovelace && requiredAssets.Count == 0)
                break;

            if (info.AssetsCovered == 0 && selectedLovelace >= requestedLovelace)
                continue;

            selectedUtxos.Add(info.Utxo);
            selectedLovelace += info.Lovelace;

            if (info.AssetsCovered > 0)
            {
                foreach (var asset in info.Assets)
                {
                    if (requiredAssets.ContainsKey(asset.Key))
                    {
                        requiredAssets[asset.Key] = requiredAssets[asset.Key] >= asset.Value ? requiredAssets[asset.Key] - asset.Value : 0;
                        if (requiredAssets[asset.Key] == 0)
                        {
                            requiredAssets.Remove(asset.Key);
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
            return;

        List<string> policies = multiAsset.PolicyId()?.ToList() ?? [];

        foreach (string policy in policies)
        {
            Dictionary<string, ulong> tokenBundle = multiAsset.TokenBundleByPolicyId(policy) ?? [];

            foreach (var token in tokenBundle)
            {
                string assetKey = (policy + token.Key).ToLowerInvariant();

                if (!assetDict.ContainsKey(assetKey))
                {
                    assetDict[assetKey] = token.Value;
                }
                else
                {
                    assetDict[assetKey] += token.Value;
                }
            }
        }
    }

    private static Dictionary<byte[], TokenBundleOutput> CalculateAssetsChange(
    List<ResolvedInput> selectedUtxos,
    List<Value> requestedAmounts)
    {
        // UPDATED: Use custom comparer for all byte array dictionaries and ulong for consistency
        var selectedAssetsByPolicy = new Dictionary<byte[], Dictionary<byte[], ulong>>(ByteArrayEqualityComparer.Instance);
        var requestedAssetsByPolicy = new Dictionary<byte[], Dictionary<byte[], ulong>>(ByteArrayEqualityComparer.Instance);

        // Process selected UTXOs
        foreach (var utxo in selectedUtxos)
        {
            if (utxo.Output.Amount() is LovelaceWithMultiAsset lovelaceWithMultiAsset)
            {
                var multiAsset = lovelaceWithMultiAsset.MultiAsset;
                if (multiAsset?.Value == null) continue;

                foreach (var policyEntry in multiAsset.Value)
                {
                    // BEFORE: This would create new dictionaries with default comparer
                    // AFTER: Use custom comparer and TryGetValue
                    if (!selectedAssetsByPolicy.TryGetValue(policyEntry.Key, out var selectedAssets))
                    {
                        selectedAssets = new Dictionary<byte[], ulong>(ByteArrayEqualityComparer.Instance);
                        selectedAssetsByPolicy[policyEntry.Key] = selectedAssets;
                    }

                    foreach (var assetEntry in policyEntry.Value.Value)
                    {
                        ulong amount = assetEntry.Value;
                        if (selectedAssets.TryGetValue(assetEntry.Key, out var existing))
                        {
                            selectedAssets[assetEntry.Key] = existing + amount;
                        }
                        else
                        {
                            selectedAssets[assetEntry.Key] = amount;
                        }
                    }
                }
            }
        }

        // Process requested amounts (same pattern)
        foreach (Value value in requestedAmounts)
        {
            if (value is LovelaceWithMultiAsset lovelaceWithMultiAsset)
            {
                var multiAsset = lovelaceWithMultiAsset.MultiAsset;
                if (multiAsset?.Value == null) continue;

                foreach (var policyEntry in multiAsset.Value)
                {
                    if (!requestedAssetsByPolicy.TryGetValue(policyEntry.Key, out var requestedAssets))
                    {
                        requestedAssets = new Dictionary<byte[], ulong>(ByteArrayEqualityComparer.Instance);
                        requestedAssetsByPolicy[policyEntry.Key] = requestedAssets;
                    }

                    foreach (var assetEntry in policyEntry.Value.Value)
                    {
                        ulong amount = assetEntry.Value;
                        if (requestedAssets.TryGetValue(assetEntry.Key, out var existing))
                        {
                            requestedAssets[assetEntry.Key] = existing + amount;
                        }
                        else
                        {
                            requestedAssets[assetEntry.Key] = amount;
                        }
                    }
                }
            }
        }

        // Calculate change using optimized lookups
        var assetsChange = new Dictionary<byte[], TokenBundleOutput>(ByteArrayEqualityComparer.Instance);

        foreach (var (policyId, selectedAssets) in selectedAssetsByPolicy)
        {
            var assetChanges = new Dictionary<byte[], ulong>(ByteArrayEqualityComparer.Instance);
            bool hasChange = false;

            foreach (var (assetName, selectedAmount) in selectedAssets)
            {
                ulong requestedAmount = 0;

                // BEFORE: Would loop through all entries looking for matches
                // AFTER: Direct lookup with custom comparer
                if (requestedAssetsByPolicy.TryGetValue(policyId, out var requestedTokens) &&
                    requestedTokens.TryGetValue(assetName, out var requested))
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
}

public record CoinSelectionResult
{
    public List<ResolvedInput> Inputs { get; set; } = [];
    public ulong LovelaceChange { get; set; }
    public Dictionary<byte[], TokenBundleOutput> AssetsChange { get; set; } = [];
}
