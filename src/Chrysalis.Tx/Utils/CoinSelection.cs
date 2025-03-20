using Chrysalis.Cbor.Cardano.Extensions;
using Chrysalis.Cbor.Cardano.Types.Block.Transaction.Output;
using Chrysalis.Cbor.Types.Primitives;
using Chrysalis.Tx.Models;

namespace Chrysalis.Tx.Utils;

public class CoinSelectionAlgorithm : ICoinSelection
{
    private const ulong UTXO_COST_PER_BYTE = 4310;
    private const ulong MINIMUM_UTXO_LOVELACE = 840_499;

    public static CoinSelectionResult LargestFirstAlgorithm(List<ResolvedInput> self, List<Value> requestedAmount, int maxInputs = 20)
    {
        if (self == null || !self.Any())
            throw new InvalidOperationException("UTxO Balance Insufficient");

        if (requestedAmount.Count <= 0)
            throw new ArgumentException("Requested amount must be greater than zero", nameof(requestedAmount));

        if (maxInputs <= 0)
            throw new ArgumentException("Maximum inputs must be greater than zero", nameof(maxInputs));

        List<ResolvedInput> sortedUtxos = [.. self.OrderByDescending(u => u.Output.Lovelace() ?? 0)];
        List<ResolvedInput> selectedUtxos = [];

        Dictionary<string, ulong> requiredAssets = [];
        foreach (Value value in requestedAmount)
        {
            if (value is LovelaceWithMultiAsset lovelaceWithMultiAsset)
            {
                List<string> policies = lovelaceWithMultiAsset.MultiAsset.PolicyId()?.ToList() ?? [];
                if (policies.Any())
                {
                    foreach (string policy in policies)
                    {
                        Dictionary<string, ulong> tokenBundle = lovelaceWithMultiAsset.MultiAsset.TokenBundleByPolicyId(policy) ?? [];

                        foreach (var token in tokenBundle)
                        {
                            requiredAssets[policy + token.Key] = token.Value;
                        }
                    }
                }

            }
        }

        ulong requestedLovelace = (ulong)requestedAmount.Select(x => (decimal)(x.Lovelace() ?? 0)).Sum();

        int iterCount = 0;
        ulong totalLovelaceSelected = 0;
        bool isAssetsCovered = requiredAssets.Count == 0;
        while (true)
        {
            if (iterCount >= self.Count)
                break;
            if (totalLovelaceSelected >= requestedLovelace && isAssetsCovered)
                break;

            if (!sortedUtxos.Any())
                throw new InvalidOperationException("UTxO Balance Insufficient");

            // Remove from head and add to selected set
            ResolvedInput utxo = sortedUtxos[0];
            sortedUtxos.RemoveAt(0);

            // Skip UTXOs with null amount or zero value
            ulong lovelace = utxo.Output.Lovelace() ?? 0;
            if (lovelace == 0) continue;

            int satisfiedAssetsCount = 0;
            foreach (var asset in requiredAssets)
            {
                var policy = asset.Key[..56];
                var name = asset.Key[56..];

                var amount = utxo.Output.QuantityOf(policy, name) ?? 0;
                if (amount == 0) continue;

                requiredAssets[asset.Key] -= amount;
                if (requiredAssets[asset.Key] <= 0)
                {
                    satisfiedAssetsCount++;
                }
            }
            if (satisfiedAssetsCount == requiredAssets.Count)
            {
                isAssetsCovered = true;
            }

            selectedUtxos.Add(utxo);
            totalLovelaceSelected += lovelace;
        }

        ulong lovelacechange = totalLovelaceSelected - requestedLovelace;

        Dictionary<string, ulong> assetsChange = requiredAssets
            .Where(x => x.Value < 0)
            .ToDictionary(x => x.Key, x => (ulong)Math.Abs((decimal)x.Value));

        return new CoinSelectionResult
        {
            Inputs = selectedUtxos,
            LovelaceChange = lovelacechange,
            AssetsChange = assetsChange
        };
    }

    public static CoinSelectionV2 HVFSelector(
        List<TransactionOutput> inputs,
        Value collectedAssets,
        Value externalAssets,
        ulong preliminaryFee = 0
    )
    {
        // Calculate total required Lovelace
        ulong requiredLovelace = (collectedAssets.Lovelace() ?? 0) + (externalAssets.Lovelace() ?? 0) + preliminaryFee;

        // Consolidate all required multi-assets
        List<Asset> requiredAssets = [];

        // Process collected assets
        if (collectedAssets.MultiAsset() != null)
        {
            requiredAssets.AddRange(MultiAssetToAssets(collectedAssets.MultiAsset() ?? []));
        }

        // Process external assets, accounting for negative values (non-required assets)
        List<Asset> nonRequiredAssets = [];
        if (externalAssets.MultiAsset() != null)
        {
            foreach (Asset asset in MultiAssetToAssets(externalAssets.MultiAsset() ?? []))
            {
                if ((long)asset.Quantity < 0)
                {
                    // Handle negative quantities as non-required assets
                    nonRequiredAssets.Add(new Asset
                    {
                        PolicyId = asset.PolicyId,
                        Name = asset.Name,
                        Quantity = (ulong)Math.Abs((long)asset.Quantity)
                    });
                }
                else
                {
                    // Add to required assets
                    requiredAssets.Add(asset);
                }
            }
        }

        // Group required assets by policy ID and name to consolidate quantities
        requiredAssets = [..requiredAssets
            .GroupBy(a => new { a.PolicyId, a.Name })
            .Select(g => new Asset
            {
                PolicyId = g.Key.PolicyId,
                Name = g.Key.Name,
                Quantity = (ulong)g.Sum(a => (long)a.Quantity)
            })];

        // Sort UTXOs by descending Lovelace value
        List<TransactionOutput> sortedUtxos = SortUTxOs(inputs, SortingOrder.Descending, requiredAssets);
        List<TransactionOutput> selectedUtxos = [];
        ulong selectedLovelace = 0;

        // Track selected asset quantities
        Dictionary<(string PolicyId, string Name), ulong> selectedAssets =
            requiredAssets.ToDictionary(asset => (asset.PolicyId, asset.Name), _ => 0UL);

        // Select UTXOs until required assets and Lovelace are covered
        foreach (TransactionOutput utxo in sortedUtxos)
        {
            // Check if we've already satisfied requirements
            bool lovelaceCovered = selectedLovelace >= requiredLovelace;
            bool assetsCovered = requiredAssets.Count == 0 ||
                requiredAssets.All(asset => selectedAssets[(asset.PolicyId, asset.Name)] >= asset.Quantity);

            if (lovelaceCovered && assetsCovered)
                break;

            // Add this UTXO to selection
            selectedUtxos.Add(utxo);
            selectedLovelace += utxo.Lovelace() ?? 0;

            // Process assets in this UTXO
            List<Asset> utxoAssets = MultiAssetToAssets(utxo.Amount()?.MultiAsset() ?? []);
            foreach (Asset asset in utxoAssets)
            {
                (string PolicyId, string Name) key = (asset.PolicyId, asset.Name);
                if (selectedAssets.ContainsKey(key))
                {
                    selectedAssets[key] += asset.Quantity;
                }
            }
        }

        // Verify selection sufficiency
        bool isLovelaceEnough = selectedLovelace >= requiredLovelace;
        bool isAssetsEnough = requiredAssets.Count == 0 ||
            requiredAssets.All(asset => selectedAssets[(asset.PolicyId, asset.Name)] >= asset.Quantity);

        if (!isLovelaceEnough || !isAssetsEnough)
        {
            throw new InvalidOperationException("UTxO Balance Insufficient for requested assets.");
        }

        // Calculate Lovelace change
        ulong lovelaceChange = selectedLovelace - requiredLovelace;

        // Calculate asset change
        Dictionary<string, Dictionary<string, ulong>> assetChangeMap = [];

        foreach (KeyValuePair<(string PolicyId, string Name), ulong> assetEntry in selectedAssets)
        {
            (string policyId, string assetName) = assetEntry.Key;
            ulong selectedQty = assetEntry.Value;
            ulong requiredQty = requiredAssets
                .FirstOrDefault(a => a.PolicyId == policyId && a.Name == assetName)?.Quantity ?? 0;

            ulong excess = selectedQty > requiredQty ? selectedQty - requiredQty : 0;

            if (excess > 0)
            {
                if (!assetChangeMap.ContainsKey(policyId))
                    assetChangeMap[policyId] = [];

                assetChangeMap[policyId][assetName] = excess;
            }
        }

        // Convert to final change format
        Dictionary<CborBytes, TokenBundleOutput> assetChange = [];
        foreach (KeyValuePair<string, Dictionary<string, ulong>> policyEntry in assetChangeMap)
        {
            CborBytes policyIdBytes = new(Convert.FromHexString(policyEntry.Key));
            TokenBundleOutput tokenBundle = new TokenBundleOutput(
                policyEntry.Value.ToDictionary(
                    a => new CborBytes(Convert.FromHexString(a.Key)),
                    a => new CborUlong(a.Value)));

            assetChange[policyIdBytes] = tokenBundle;
        }

        LovelaceWithMultiAsset finalChange = new(
            new Lovelace(lovelaceChange),
            new MultiAssetOutput(assetChange)
        );

        return new CoinSelectionV2
        {
            SelectedUTxOs = selectedUtxos,
            Inputs = selectedUtxos,
            Change = (finalChange.Lovelace() > 0 || finalChange.MultiAsset != null)
                ? finalChange
                : default!
        };
    }

    public static CoinSelection LargestValueFirstAlgorithm(
        List<UnspentTransactionOutput> inputs,
        Value collectedAssets,
        Value externalAssets,
        ulong preliminaryFee = 0)
    {
        ulong requiredLovelace = (collectedAssets.Lovelace() ?? 0) + (externalAssets.Lovelace() ?? 0) + preliminaryFee;

        // Consolidate all required multi-assets into a grouped list.
        List<Asset> requiredAssets = [.. MultiAssetToAssets(collectedAssets.MultiAsset() ?? [])
            .Concat(MultiAssetToAssets(externalAssets.MultiAsset() ?? []))
            .GroupBy(a => new { a.PolicyId, a.Name })
            .Select(g => new Asset
            {
                PolicyId = g.Key.PolicyId,
                Name = g.Key.Name,
                Quantity = (ulong)g.Sum(a => (long)a.Quantity)
            })];

        // Sort UTXOs by descending Lovelace, then total asset quantities.
        // List<TransactionOutput> sortedUtxos = SortUTxOs(inputs, SortingOrder.Descending, requiredAssets);
        List<UnspentTransactionOutput> sortedUtxos = SortLargestFirst(inputs);
        // List<UnspentTransactionOutput> sortedUtxos = SortUTxOsByCompositeScore(inputs, requiredAssets, requiredLovelace, assetWeight: 1.0);

        Dictionary<string, ulong> totalSelectedAssets =
            requiredAssets.ToDictionary(asset => asset.PolicyId + asset.Name, asset => asset.Quantity);

        List<UnspentTransactionOutput> selectedUtxos = [];
        ulong totalSelectedLovelace = 0;

        // Select UTXOs until the required assets and Lovelace are covered.
        sortedUtxos.ForEach(utxo =>
        {
            bool assetsCovered = requiredAssets
                .Any(asset => totalSelectedAssets[asset.PolicyId + asset.Name] >= asset.Quantity);

            if (totalSelectedLovelace >= requiredLovelace && assetsCovered)
                return;

            selectedUtxos.Add(utxo);
            totalSelectedLovelace += utxo.Amount.Lovelace() ?? 0;

            MultiAssetToAssets(utxo.Amount.MultiAsset() ?? [])
                .Where(asset => totalSelectedAssets.ContainsKey(asset.PolicyId + asset.Name))
                .ToList()
                .ForEach(asset => totalSelectedAssets[asset.PolicyId + asset.Name] += asset.Quantity);
        });

        // Validate asset coverage
        if (totalSelectedLovelace < requiredLovelace ||
            requiredAssets.Any(asset => totalSelectedAssets[asset.PolicyId + asset.Name] < asset.Quantity))
            throw new InvalidOperationException("UTxO Balance Insufficient for requested assets.");

        // Calculate Lovelace change
        ulong lovelaceChange = totalSelectedLovelace - requiredLovelace;

        // Calculate asset change
        Dictionary<CborBytes, TokenBundleOutput> assetChange = requiredAssets
            .Select(asset => new
            {
                asset.PolicyId,
                asset.Name,
                ExcessQuantity = totalSelectedAssets[asset.PolicyId + asset.Name] - asset.Quantity
            })
            .Where(a => a.ExcessQuantity > 0)
            .GroupBy(a => a.PolicyId)
            .ToDictionary(
                g => new CborBytes(Convert.FromHexString(g.Key)),
                g => new TokenBundleOutput(g.ToDictionary(x => new CborBytes(Convert.FromHexString(x.Name)), x => new CborUlong(x.ExcessQuantity))));

        MultiAssetOutput? multiAssetOutput = assetChange.Any()
            ? new MultiAssetOutput(assetChange)
            : null;

        LovelaceWithMultiAsset changeValue = new(
            new Lovelace(lovelaceChange),
            multiAssetOutput!
        );

        return new CoinSelection
        {
            SelectedUTxOs = selectedUtxos,
            Inputs = selectedUtxos,
            Change = changeValue.Lovelace() > 0 || changeValue.MultiAsset is not null
                ? changeValue
                : default!
        };
    }

    protected static List<TransactionOutput> SortUTxOs(
        List<TransactionOutput> utxos,
        SortingOrder sortingOrder = SortingOrder.Descending,
        List<Asset>? assets = null)
    {
        List<TransactionOutput> orderedUtxos = [];
        if (assets is null || !assets.Any())
        {
            return orderedUtxos = sortingOrder == SortingOrder.Descending
                ? [.. utxos.OrderByDescending(x => x.Lovelace() ?? 0)]
                : [.. utxos.OrderBy(x => x.Lovelace() ?? 0)];
        }

        var utxosWithQuantity = utxos
        .Select(x => new
        {
            Output = x,
            AssetQuantity = (ulong)MultiAssetToAssets(x.Amount()?.MultiAsset() ?? [])
                .Where(ma => assets.Any(a => a.PolicyId == ma.PolicyId && a.Name == ma.Name))
                .Sum(ma => (double)ma.Quantity)
        })
        .Where(x => x.AssetQuantity > 0);

        orderedUtxos = orderedUtxos = sortingOrder == SortingOrder.Descending
            ? [.. utxosWithQuantity
                .OrderByDescending(x => x.AssetQuantity)
                .ThenByDescending(x => x.Output.Lovelace())
                .Select(x => x.Output)]
            : [.. utxosWithQuantity
                .OrderBy(x => x.AssetQuantity)
                .ThenBy(x => x.Output.Lovelace())
                .Select(x => x.Output)];

        return orderedUtxos;
    }

    private static List<TransactionOutput> SortUTxOsByCompositeScore(
        List<TransactionOutput> utxos,
        List<Asset> requiredAssets,
        ulong requiredLovelace,
        double assetWeight = 1.0)
    {
        return [.. utxos
            .Select(utxo =>
            {
                ulong coin = utxo.Amount()?.Lovelace() ?? 0;
                // Normalize coin score: coin divided by requiredLovelace.
                double coinScore = requiredLovelace > 0 ? (double)coin / requiredLovelace : 0;
                
                // Compute asset score by summing the ratios for each required asset.
                double assetScore = 0;
                var utxoAssets = MultiAssetToAssets(utxo.Amount()?.MultiAsset() ?? []);
                foreach (var reqAsset in requiredAssets)
                {
                    var matching = utxoAssets.FirstOrDefault(a => a.PolicyId == reqAsset.PolicyId && a.Name == reqAsset.Name);
                    if (matching != null)
                    {
                        // Compute ratio, capping at 1.0 for each asset.
                        double ratio = Math.Min(1.0, (double)matching.Quantity / reqAsset.Quantity);
                        assetScore += ratio;
                    }
                }
                double compositeScore = coinScore + assetWeight * assetScore;
                return new { Utxo = utxo, Score = compositeScore };
            })
            .OrderByDescending(x => x.Score)
            .Select(x => x.Utxo)];
    }

    // https://github.com/Anastasia-Labs/lucid-evolution/blob/dfbd1aed2d3aa78dd5181061da8f977f08bf9814/packages/utils/src/utxo.ts#L112
    public static IEnumerable<TransactionOutput> SelectUTxOs(
        IEnumerable<TransactionOutput> transactionOutputs,
        Value totalAssets,
        ulong lovelace,
        bool includeUTxOsWithScriptRef = false)
    {
        List<TransactionOutput> selectedUtxos = [];
        List<Asset> assetsRequired =
        [
            .. MultiAssetToAssets(totalAssets.MultiAsset() ?? []),
            new Asset { PolicyId = string.Empty, Name = string.Empty, Quantity = lovelace },
        ];

        List<TransactionOutput> sortedOutputs = [.. SortUTxOs([.. transactionOutputs], SortingOrder.Descending)];

        bool isSelected = false;
        transactionOutputs.ToList().ForEach(output =>
        {
            if (!includeUTxOsWithScriptRef && output.ScriptRef() is not null) return;
            isSelected = false;

            List<Asset> tempAssets = [.. assetsRequired];

            tempAssets.ForEach(asset =>
            {
                ulong utxoAmount = asset.PolicyId == string.Empty && asset.Name == string.Empty
                    ? output.Lovelace() ?? 0
                    : output
                        .Amount()?
                        .MultiAsset()?
                        .GetValueOrDefault(asset.PolicyId)?
                        .TokenBundle()
                        .GetValueOrDefault(asset.Name) ?? 0;

                if (utxoAmount == 0) return;

                isSelected = true;

                if (utxoAmount >= asset.Quantity)
                    assetsRequired.Remove(asset);
                else
                    asset.Quantity -= utxoAmount;
            });

            if (isSelected) selectedUtxos.Add(output);

            if (!assetsRequired.Any()) return;
        });

        return assetsRequired.Any() ? [] : selectedUtxos;
    }

    public static List<UnspentTransactionOutput> SortLargestFirst(List<UnspentTransactionOutput> inputs)
    {
        return [.. inputs
            .OrderByDescending(tx => tx.Amount.Lovelace() ?? 0)
            .ThenByDescending(tx => (tx.Amount.MultiAsset()?.Count ?? 0) + 1)
            .Where(tx => tx.Amount.Lovelace() > 0)];
    }

    public static List<Asset> MultiAssetToAssets(Dictionary<string, TokenBundleOutput> multiAsset)
    {
        return [.. multiAsset.SelectMany(policy =>
            policy.Value.TokenBundle().Select(asset => new Asset
            {
                PolicyId = policy.Key,
                Name = asset.Key,
                Quantity = asset.Value
            }))];
    }
}

public record Asset
{
    public required string PolicyId { get; set; }
    public required string Name { get; set; }
    public ulong Quantity { get; set; }
}

public interface ICoinSelection { };

public record CoinSelectionResult
{
    public List<ResolvedInput> Inputs { get; set; } = [];
    public ulong LovelaceChange { get; set; }
    public Dictionary<string, ulong> AssetsChange { get; set; } = [];
}

public enum SortingOrder
{
    Ascending,
    Descending
}

public record CoinSelectionV2
{
    public List<TransactionOutput> SelectedUTxOs { get; set; } = [];
    public List<TransactionOutput> Inputs { get; set; } = [];
    public Value Change { get; set; } = default!;
}

public record CoinSelection
{
    public List<UnspentTransactionOutput> SelectedUTxOs { get; set; } = [];
    public List<UnspentTransactionOutput> Inputs { get; set; } = [];
    public Value Change { get; set; } = default!;
}

public record UnspentTransactionOutput
{
    public string TxHash { get; set; } = default!;
    public string TxIndex { get; set; } = default!;
    public string Address { get; set; } = default!;
    public Value Amount { get; set; } = default!;
    public string? ScriptRef { get; set; }
};
