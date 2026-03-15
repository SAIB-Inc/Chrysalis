using Chrysalis.Codec.Extensions.Cardano.Core.Common;
using Chrysalis.Codec.Extensions.Cardano.Core.Transaction;
using Chrysalis.Codec.Serialization.Utils;
using Chrysalis.Codec.Types.Cardano.Core.Common;
using Chrysalis.Tx.Extensions;
using Chrysalis.Tx.Models.Cbor;

namespace Chrysalis.Tx.Utils;

/// <summary>
/// Available coin selection strategies.
/// </summary>
public enum CoinSelectionStrategy
{
    /// <summary>Greedy largest-first selection. Simple, deterministic.</summary>
    LargestFirst,

    /// <summary>CIP-2 random-improve with multi-asset support. Minimizes change fragmentation.</summary>
    RandomImprove,
}

/// <summary>
/// Implements coin selection algorithms for Cardano transaction building.
/// </summary>
public static class CoinSelectionUtil
{
    /// <summary>
    /// Selects UTxOs using the specified strategy to satisfy the requested amounts.
    /// </summary>
    public static CoinSelectionResult Select(
        List<ResolvedInput> availableUtxos,
        List<IValue> requestedAmount,
        CoinSelectionStrategy strategy = CoinSelectionStrategy.LargestFirst,
        int maxInputs = int.MaxValue) => strategy switch
        {
            CoinSelectionStrategy.LargestFirst => LargestFirstAlgorithm(availableUtxos, requestedAmount, maxInputs),
            CoinSelectionStrategy.RandomImprove => RandomImproveAlgorithm(availableUtxos, requestedAmount, maxInputs),
            _ => LargestFirstAlgorithm(availableUtxos, requestedAmount, maxInputs),
        };

    /// <summary>
    /// Selects UTxOs using the largest-first algorithm to satisfy the requested amounts.
    /// </summary>
    /// <param name="availableUtxos">The available UTxOs to select from.</param>
    /// <param name="requestedAmount">The required output values.</param>
    /// <param name="maxInputs">Maximum number of inputs to select.</param>
    /// <returns>A coin selection result with selected inputs and change.</returns>
    public static CoinSelectionResult LargestFirstAlgorithm(
    List<ResolvedInput> availableUtxos,
    List<IValue> requestedAmount,
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

        foreach (IValue value in requestedAmount)
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
        Dictionary<ReadOnlyMemory<byte>, TokenBundleOutput> assetsChange = CalculateAssetsChange(selectedUtxos, requestedAmount);

        return new CoinSelectionResult
        {
            Inputs = selectedUtxos,
            LovelaceChange = lovelaceChange,
            AssetsChange = assetsChange
        };
    }

    /// <summary>
    /// CIP-2 random-improve coin selection with multi-asset support.
    /// Three phases: random selection per asset, improvement swaps, ADA coverage.
    /// </summary>
    public static CoinSelectionResult RandomImproveAlgorithm(
        List<ResolvedInput> availableUtxos,
        List<IValue> requestedAmount,
        int maxInputs = int.MaxValue)
    {
        if (availableUtxos == null || availableUtxos.Count == 0)
        {
            throw new InvalidOperationException("UTxO Balance Insufficient");
        }

        ArgumentNullException.ThrowIfNull(requestedAmount);

        ulong requestedLovelace = 0;
        Dictionary<string, ulong> requiredAssets = [];

        foreach (IValue value in requestedAmount)
        {
            requestedLovelace += value.Lovelace();
            if (value is LovelaceWithMultiAsset lma)
            {
                ExtractAssets(lma.MultiAsset, requiredAssets);
            }
        }

        List<int> availableIndices = [.. Enumerable.Range(0, availableUtxos.Count)];
        HashSet<int> selectedIndices = [];

        // Phase 1: Random selection — for each asset, randomly pick UTxOs until covered
        // First cover multi-asset requirements
        foreach (KeyValuePair<string, ulong> required in requiredAssets)
        {
            ulong remaining = required.Value;
            List<int> candidates = [.. availableIndices
                .Where(i => !selectedIndices.Contains(i) && HasAsset(availableUtxos[i], required.Key))];

            Shuffle(candidates);

            foreach (int idx in candidates)
            {
                if (remaining == 0 || selectedIndices.Count >= maxInputs)
                {
                    break;
                }

                _ = selectedIndices.Add(idx);
                ulong amount = GetAssetAmount(availableUtxos[idx], required.Key);
                remaining = remaining > amount ? remaining - amount : 0;
            }

            if (remaining > 0)
            {
                throw new InvalidOperationException($"UTxO Balance Insufficient - missing asset: {required.Key}");
            }
        }

        // Cover lovelace
        ulong selectedLovelace = 0;
        foreach (int i in selectedIndices)
        {
            selectedLovelace += availableUtxos[i].Output.Amount().Lovelace();
        }
        if (selectedLovelace < requestedLovelace)
        {
            List<int> lovelaceCandidates = [.. availableIndices
                .Where(i => !selectedIndices.Contains(i))];

            Shuffle(lovelaceCandidates);

            foreach (int idx in lovelaceCandidates)
            {
                if (selectedLovelace >= requestedLovelace || selectedIndices.Count >= maxInputs)
                {
                    break;
                }

                _ = selectedIndices.Add(idx);
                selectedLovelace += availableUtxos[idx].Output.Amount().Lovelace();
            }
        }

        if (selectedLovelace < requestedLovelace)
        {
            throw new InvalidOperationException($"UTxO Balance Insufficient - need {requestedLovelace} lovelace but only found {selectedLovelace}");
        }

        // Phase 2: Improvement — try swapping selected inputs with remaining ones
        // to get closer to ideal target (2x requested, capped at 3x)
        ulong idealLovelace = requestedLovelace * 2;
        ulong maxLovelace = requestedLovelace * 3;

        List<int> remaining2 = [.. availableIndices.Where(i => !selectedIndices.Contains(i))];
        Shuffle(remaining2);

        foreach (int candidateIdx in remaining2)
        {
            if (selectedIndices.Count >= maxInputs)
            {
                break;
            }

            ulong candidateLovelace = availableUtxos[candidateIdx].Output.Amount().Lovelace();
            ulong newTotal = selectedLovelace + candidateLovelace;

            // Only add if it improves (gets closer to ideal without exceeding max)
            if (newTotal <= maxLovelace &&
                Math.Abs((long)newTotal - (long)idealLovelace) < Math.Abs((long)selectedLovelace - (long)idealLovelace))
            {
                _ = selectedIndices.Add(candidateIdx);
                selectedLovelace = newTotal;
            }
        }

        // Phase 3: Build result
        List<ResolvedInput> selectedUtxos = [.. selectedIndices.Select(i => availableUtxos[i])];
        ulong lovelaceChange = selectedLovelace - requestedLovelace;
        Dictionary<ReadOnlyMemory<byte>, TokenBundleOutput> assetsChange = CalculateAssetsChange(selectedUtxos, requestedAmount);

        return new CoinSelectionResult
        {
            Inputs = selectedUtxos,
            LovelaceChange = lovelaceChange,
            AssetsChange = assetsChange
        };
    }

    private static bool HasAsset(ResolvedInput utxo, string assetKey)
    {
        if (utxo.Output.Amount() is not LovelaceWithMultiAsset lma)
        {
            return false;
        }

        Dictionary<string, ulong> assets = [];
        ExtractAssets(lma.MultiAsset, assets);
        return assets.ContainsKey(assetKey);
    }

    private static ulong GetAssetAmount(ResolvedInput utxo, string assetKey)
    {
        if (utxo.Output.Amount() is not LovelaceWithMultiAsset lma)
        {
            return 0;
        }

        Dictionary<string, ulong> assets = [];
        ExtractAssets(lma.MultiAsset, assets);
        return assets.TryGetValue(assetKey, out ulong amount) ? amount : 0;
    }

    private static void Shuffle(List<int> list)
    {
        for (int i = list.Count - 1; i > 0; i--)
        {
            int j = System.Security.Cryptography.RandomNumberGenerator.GetInt32(i + 1);
            (list[i], list[j]) = (list[j], list[i]);
        }
    }

    private static void ExtractAssets(
        MultiAssetOutput multiAsset,
        Dictionary<string, ulong> assetDict)
    {
        if (multiAsset.Value == null)
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

    private static Dictionary<ReadOnlyMemory<byte>, TokenBundleOutput> CalculateAssetsChange(
    List<ResolvedInput> selectedUtxos,
    List<IValue> requestedAmounts)
    {
        Dictionary<ReadOnlyMemory<byte>, Dictionary<ReadOnlyMemory<byte>, ulong>> selectedAssetsByPolicy = new(ReadOnlyMemoryComparer.Instance);
        Dictionary<ReadOnlyMemory<byte>, Dictionary<ReadOnlyMemory<byte>, ulong>> requestedAssetsByPolicy = new(ReadOnlyMemoryComparer.Instance);

        // Process selected UTXOs
        foreach (ResolvedInput utxo in selectedUtxos)
        {
            if (utxo.Output.Amount() is LovelaceWithMultiAsset lovelaceWithMultiAsset)
            {
                MultiAssetOutput multiAsset = lovelaceWithMultiAsset.MultiAsset;
                if (multiAsset.Value == null)
                {
                    continue;
                }

                foreach (KeyValuePair<ReadOnlyMemory<byte>, TokenBundleOutput> policyEntry in multiAsset.Value)
                {
                    if (!selectedAssetsByPolicy.TryGetValue(policyEntry.Key, out Dictionary<ReadOnlyMemory<byte>, ulong>? selectedAssets))
                    {
                        selectedAssets = new Dictionary<ReadOnlyMemory<byte>, ulong>(ReadOnlyMemoryComparer.Instance);
                        selectedAssetsByPolicy[policyEntry.Key] = selectedAssets;
                    }

                    foreach (KeyValuePair<ReadOnlyMemory<byte>, ulong> assetEntry in policyEntry.Value.Value)
                    {
                        ulong amount = assetEntry.Value;
                        selectedAssets[assetEntry.Key] = selectedAssets.TryGetValue(assetEntry.Key, out ulong existing) ? existing + amount : amount;
                    }
                }
            }
        }

        // Process requested amounts
        foreach (IValue value in requestedAmounts)
        {
            if (value is LovelaceWithMultiAsset lovelaceWithMultiAsset)
            {
                MultiAssetOutput multiAsset = lovelaceWithMultiAsset.MultiAsset;
                if (multiAsset.Value == null)
                {
                    continue;
                }

                foreach (KeyValuePair<ReadOnlyMemory<byte>, TokenBundleOutput> policyEntry in multiAsset.Value)
                {
                    if (!requestedAssetsByPolicy.TryGetValue(policyEntry.Key, out Dictionary<ReadOnlyMemory<byte>, ulong>? requestedAssets))
                    {
                        requestedAssets = new Dictionary<ReadOnlyMemory<byte>, ulong>(ReadOnlyMemoryComparer.Instance);
                        requestedAssetsByPolicy[policyEntry.Key] = requestedAssets;
                    }

                    foreach (KeyValuePair<ReadOnlyMemory<byte>, ulong> assetEntry in policyEntry.Value.Value)
                    {
                        ulong amount = assetEntry.Value;
                        requestedAssets[assetEntry.Key] = requestedAssets.TryGetValue(assetEntry.Key, out ulong existing) ? existing + amount : amount;
                    }
                }
            }
        }

        // Calculate change using optimized lookups
        Dictionary<ReadOnlyMemory<byte>, TokenBundleOutput> assetsChange = new(ReadOnlyMemoryComparer.Instance);

        foreach ((ReadOnlyMemory<byte> policyId, Dictionary<ReadOnlyMemory<byte>, ulong> selectedAssets) in selectedAssetsByPolicy)
        {
            Dictionary<ReadOnlyMemory<byte>, ulong> assetChanges = new(ReadOnlyMemoryComparer.Instance);
            bool hasChange = false;

            foreach ((ReadOnlyMemory<byte> assetName, ulong selectedAmount) in selectedAssets)
            {
                ulong requestedAmount = 0;

                if (requestedAssetsByPolicy.TryGetValue(policyId, out Dictionary<ReadOnlyMemory<byte>, ulong>? requestedTokens) &&
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
                assetsChange[policyId] = TokenBundleOutput.Create(assetChanges);
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
    public Dictionary<ReadOnlyMemory<byte>, TokenBundleOutput> AssetsChange { get; init; } = [];
}
