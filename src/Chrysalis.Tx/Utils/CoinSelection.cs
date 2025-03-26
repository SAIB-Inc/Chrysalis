using Chrysalis.Cbor.Types.Cardano.Core.Common;
using Chrysalis.Cbor.Types.Cardano.Core.Transaction;
using Chrysalis.Tx.Extension;
using Chrysalis.Tx.Extensions;
using Chrysalis.Tx.Models;

namespace Chrysalis.Tx.Utils;

public class CoinSelectionAlgorithm : ICoinSelection
{
    public static CoinSelectionResult LargestFirstAlgorithm(List<ResolvedInput> self, List<Value> requestedAmount, Value? minimumAmount = null, List<ResolvedInput>? specifiedInputs = null, int maxInputs = 20)
    {
        if (self == null || !self.Any())
            throw new InvalidOperationException("UTxO Balance Insufficient");

        if (requestedAmount.Count <= 0)
            throw new ArgumentException("Requested amount must be greater than zero", nameof(requestedAmount));

        if (maxInputs <= 0)
            throw new ArgumentException("Maximum inputs must be greater than zero", nameof(maxInputs));

        List<ResolvedInput> sortedUtxos = [.. self.OrderByDescending(u => u.Output.Amount().Lovelace())];
        List<ResolvedInput> selectedUtxos = [];

        Dictionary<string, decimal> requiredAssets = [];
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

        ulong specifiedInputsLovelace = 0;
        if(specifiedInputs != null)
        {
            foreach (var utxo in specifiedInputs)
            {
                specifiedInputsLovelace += utxo.Output.Amount().Lovelace();
            }
        }
        

        ulong requestedLovelace = (ulong)requestedAmount.Select(x => (decimal)x.Lovelace()).Sum() - specifiedInputsLovelace;
        int iterCount = 0;
        ulong totalLovelaceSelected = 0;
        Dictionary<string, decimal> coinSelectedAssets = new(requiredAssets);
        while (true)
        {
            if (iterCount >= self.Count)
                break;
            if (totalLovelaceSelected >= requestedLovelace && requiredAssets.Count == 0)
                break;

            if (!sortedUtxos.Any())
                throw new InvalidOperationException("UTxO Balance Insufficient");

            // Remove from head and add to selected set
            ResolvedInput utxo = sortedUtxos[0];
            sortedUtxos.RemoveAt(0);

            // Skip UTXOs with null amount or zero value
            ulong lovelace = utxo.Output.Lovelace();
            if (lovelace == 0) continue;
            if (lovelace < (minimumAmount?.Lovelace() ?? 0)) continue;

            bool isAssetPresent = false;
            foreach (var asset in requiredAssets)
            {
                var policy = asset.Key[..56];
                var name = asset.Key[56..];
                var matchedPolicy = utxo.Output.Amount()?.MultiAssetOutput()?.TokenBundleByPolicyId(policy);
                ulong amount = 0;
                foreach (var item in matchedPolicy ?? [])
                {
                    if (item.Key == name)
                    {
                        amount = item.Value;
                        break;
                    }
                }
                if (amount == 0) continue;
                isAssetPresent = true;
                requiredAssets[asset.Key] -= amount;
                if (requiredAssets[asset.Key] <= 0)
                {
                    coinSelectedAssets[asset.Key] = requiredAssets[asset.Key];
                    requiredAssets.Remove(asset.Key);
                }
            }

            if (!isAssetPresent && totalLovelaceSelected >= requestedLovelace) continue;

            if (utxo.Output.Amount() is LovelaceWithMultiAsset)
            {
                var utxoOutput = utxo.Output.Amount()!.MultiAssetOutput()!;
                List<string> policies = utxoOutput.PolicyId()?.ToList()!;
                foreach (string policy in policies)
                {
                    Dictionary<string, ulong> tokenBundle = utxoOutput.TokenBundleByPolicyId(policy)!;

                    foreach (var token in tokenBundle)
                    {
                        var subject = policy + token.Key;
                        if (coinSelectedAssets.ContainsKey(subject)) continue;
                        coinSelectedAssets[subject] = token.Value;

                    }
                }
            }

            selectedUtxos.Add(utxo);
            totalLovelaceSelected += lovelace;
        }

        ulong lovelacechange = totalLovelaceSelected - requestedLovelace;
        Dictionary<byte[], TokenBundleOutput> assetsChange = [];

        Dictionary<string, Dictionary<string, decimal>> assetChangeMap = [];

        foreach (var asset in coinSelectedAssets)
        {
            var policy = asset.Key[..56];
            var name = asset.Key[56..];
            {
                if (!assetChangeMap.ContainsKey(policy))
                {
                    assetChangeMap[policy] = new()
                    {
                        { name, Math.Abs(asset.Value) }
                    };
                }
                else
                {
                    if (!assetChangeMap[policy].ContainsKey(name))
                    {
                        assetChangeMap[policy][name] = Math.Abs(asset.Value);
                    }
                    else
                    {
                        assetChangeMap[policy][name] += Math.Abs(asset.Value);
                    }
                }
            }
        }


        foreach (var asset in assetChangeMap)
        {
            var policyIdBytes = Convert.FromHexString(asset.Key);
            var tokenBundle = new TokenBundleOutput(asset.Value.ToDictionary(x => Convert.FromHexString(x.Key), x => (ulong)x.Value));
            assetsChange[policyIdBytes] = tokenBundle;
        }

        return new CoinSelectionResult
        {
            Inputs = selectedUtxos,
            LovelaceChange = lovelacechange,
            AssetsChange = assetsChange
        };
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
    public Dictionary<byte[], TokenBundleOutput> AssetsChange { get; set; } = [];
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
