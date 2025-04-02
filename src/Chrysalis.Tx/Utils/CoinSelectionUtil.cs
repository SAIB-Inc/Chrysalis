using Chrysalis.Cbor.Extensions.Cardano.Core.Common;
using Chrysalis.Cbor.Extensions.Cardano.Core.Transaction;
using Chrysalis.Cbor.Types.Cardano.Core.Common;
using Chrysalis.Tx.Extensions;
using Chrysalis.Tx.Models;

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
        Dictionary<string, decimal> requiredAssets = [];
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
            Dictionary<string, decimal> utxoAssets = [];

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
                        requiredAssets[asset.Key] -= asset.Value;
                        if (requiredAssets[asset.Key] <= 0)
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
        Dictionary<string, decimal> assetDict)
    {
        if (multiAsset == null || multiAsset.Value == null)
            return;

        List<string> policies = multiAsset.PolicyId()?.ToList() ?? [];

        foreach (string policy in policies)
        {
            Dictionary<string, ulong> tokenBundle = multiAsset.TokenBundleByPolicyId(policy) ?? [];

            foreach (var token in tokenBundle)
            {
                string assetKey = policy + token.Key;

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
        Dictionary<string, Dictionary<string, decimal>> selectedAssetsByPolicy = [];

        foreach (var utxo in selectedUtxos)
        {
            if (utxo.Output.Amount() is LovelaceWithMultiAsset lovelaceWithMultiAsset)
            {
                var multiAsset = lovelaceWithMultiAsset.MultiAsset;
                if (multiAsset == null || multiAsset.Value == null) continue;

                foreach (var policyEntry in multiAsset.Value)
                {
                    string policyId = Convert.ToHexString(policyEntry.Key).ToLowerInvariant();
                    var tokenBundle = policyEntry.Value;

                    if (!selectedAssetsByPolicy.ContainsKey(policyId))
                    {
                        selectedAssetsByPolicy[policyId] = [];
                    }

                    foreach (var assetEntry in tokenBundle.Value)
                    {
                        string assetName = Convert.ToHexString(assetEntry.Key).ToLowerInvariant();
                        decimal amount = assetEntry.Value;

                        if (!selectedAssetsByPolicy[policyId].ContainsKey(assetName))
                        {
                            selectedAssetsByPolicy[policyId][assetName] = amount;
                        }
                        else
                        {
                            selectedAssetsByPolicy[policyId][assetName] += amount;
                        }
                    }
                }
            }
        }

        Dictionary<string, Dictionary<string, decimal>> requestedAssetsByPolicy = [];

        foreach (Value value in requestedAmounts)
        {
            if (value is LovelaceWithMultiAsset lovelaceWithMultiAsset)
            {
                var multiAsset = lovelaceWithMultiAsset.MultiAsset;
                if (multiAsset == null || multiAsset.Value == null) continue;

                foreach (var policyEntry in multiAsset.Value)
                {
                    string policyId = Convert.ToHexString(policyEntry.Key).ToLowerInvariant();
                    var tokenBundle = policyEntry.Value;

                    if (!requestedAssetsByPolicy.ContainsKey(policyId))
                    {
                        requestedAssetsByPolicy[policyId] = [];
                    }

                    foreach (var assetEntry in tokenBundle.Value)
                    {
                        string assetName = Convert.ToHexString(assetEntry.Key).ToLowerInvariant();
                        decimal amount = assetEntry.Value;

                        if (!requestedAssetsByPolicy[policyId].ContainsKey(assetName))
                        {
                            requestedAssetsByPolicy[policyId][assetName] = amount;
                        }
                        else
                        {
                            requestedAssetsByPolicy[policyId][assetName] += amount;
                        }
                    }
                }
            }
        }

        Dictionary<byte[], TokenBundleOutput> assetsChange = [];

        foreach (var policyEntry in selectedAssetsByPolicy)
        {
            string policyId = policyEntry.Key;
            var selectedAssets = policyEntry.Value;

            Dictionary<string, ulong> assetChangesByName = [];
            bool hasChange = false;

            foreach (var assetEntry in selectedAssets)
            {
                string assetName = assetEntry.Key;
                decimal selectedAmount = assetEntry.Value;

                decimal requestedAssetAmount = 0;
                if (requestedAssetsByPolicy.ContainsKey(policyId) &&
                    requestedAssetsByPolicy[policyId].ContainsKey(assetName))
                {
                    requestedAssetAmount = requestedAssetsByPolicy[policyId][assetName];
                }

                decimal change = selectedAmount - requestedAssetAmount;
                if (change > 0)
                {
                    assetChangesByName[assetName] = (ulong)change;
                    hasChange = true;
                }
            }

            if (hasChange)
            {
                Dictionary<byte[], ulong> assetChanges = [];
                foreach (var change in assetChangesByName)
                {
                    assetChanges[Convert.FromHexString(change.Key)] = change.Value;
                }

                assetsChange[Convert.FromHexString(policyId)] = new TokenBundleOutput(assetChanges);
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
