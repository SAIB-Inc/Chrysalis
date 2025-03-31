using Chrysalis.Cbor.Extensions.Cardano.Core.Common;
using Chrysalis.Cbor.Extensions.Cardano.Core.Transaction;
using Chrysalis.Cbor.Types.Cardano.Core.Common;
using Chrysalis.Cbor.Types.Cardano.Core.Transaction;
using Chrysalis.Tx.Extensions;
using Chrysalis.Tx.Models;

namespace Chrysalis.Tx.Utils;

//@TODO: Unit Tests
public static class CoinSelectionUtil
{
    public static CoinSelectionResult LargestFirstAlgorithm(List<ResolvedInput> self, List<Value> requestedAmount, Value? minimumAmount = null, List<ResolvedInput>? specifiedInputs = null, int maxInputs = 20)
    {
        if (self == null || self.Count == 0)
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
                MultiAssetOutput multiAsset = new(lovelaceWithMultiAsset.MultiAsset());
                List<string> policies = multiAsset.PolicyId()?.ToList() ?? [];
                if (policies.Count != 0)
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
        if (specifiedInputs != null)
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

            if (sortedUtxos.Count == 0)
                throw new InvalidOperationException("UTxO Balance Insufficient");

            // Remove from head and add to selected set
            ResolvedInput utxo = sortedUtxos[0];
            sortedUtxos.RemoveAt(0);

            // Skip UTXOs with null amount or zero value
            ulong lovelace = utxo.Output.Amount().Lovelace();
            if (lovelace == 0) continue;
            if (lovelace < (minimumAmount?.Lovelace() ?? 0)) continue;

            bool isAssetPresent = false;
            foreach (var asset in requiredAssets)
            {
                string policy = asset.Key[..56];
                string name = asset.Key[56..];
                MultiAssetOutput? multiAsset = new(utxo.Output.Amount().MultiAsset());
                var matchedPolicy = multiAsset.TokenBundleByPolicyId(policy);
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
                MultiAssetOutput utxoOutput = new(utxo.Output.Amount()!.MultiAsset());
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

public record CoinSelectionResult
{
    public List<ResolvedInput> Inputs { get; set; } = [];
    public ulong LovelaceChange { get; set; }
    public Dictionary<byte[], TokenBundleOutput> AssetsChange { get; set; } = [];
}
