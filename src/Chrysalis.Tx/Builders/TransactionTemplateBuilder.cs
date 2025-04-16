﻿using Chrysalis.Cbor.Extensions;
using Chrysalis.Cbor.Extensions.Cardano.Core.Common;
using Chrysalis.Cbor.Extensions.Cardano.Core.Transaction;
using Chrysalis.Cbor.Serialization;
using Chrysalis.Cbor.Types;
using Chrysalis.Cbor.Types.Cardano.Core.Certificates;
using Chrysalis.Cbor.Types.Cardano.Core.Common;
using Chrysalis.Cbor.Types.Cardano.Core.Protocol;
using Chrysalis.Cbor.Types.Cardano.Core.Transaction;
using Chrysalis.Cbor.Types.Cardano.Core.TransactionWitness;
using Chrysalis.Tx.Extensions;
using Chrysalis.Tx.Models;
using Chrysalis.Tx.Utils;
using WalletAddress = Chrysalis.Wallet.Models.Addresses.Address;

namespace Chrysalis.Tx.Builders;

public class TransactionTemplateBuilder<T>
{
    private ICardanoDataProvider? _provider;
    private string? _changeAddress;
    private readonly Dictionary<string, string> _staticParties = [];
    private readonly List<Action<InputOptions<T>, T>> _inputConfigs = [];
    private readonly List<Action<ReferenceInputOptions, T>> _referenceInputConfigs = [];
    private readonly List<Action<OutputOptions, T>> _outputConfigs = [];
    private readonly List<Action<MintOptions<T>, T>> _mintConfigs = [];
    private readonly List<Action<WithdrawalOptions<T>, T>> _withdrawalConfigs = [];
    private readonly List<string> requiredSigners = [];
    private ulong _validFrom;
    private ulong _validTo;

    public static TransactionTemplateBuilder<T> Create(ICardanoDataProvider provider) => new TransactionTemplateBuilder<T>().SetProvider(provider);

    private TransactionTemplateBuilder<T> SetProvider(ICardanoDataProvider provider)
    {
        _provider = provider;
        return this;
    }

    public TransactionTemplateBuilder<T> AddInput(Action<InputOptions<T>, T> config)
    {
        _inputConfigs.Add(config);
        return this;
    }

    public TransactionTemplateBuilder<T> AddReferenceInput(Action<ReferenceInputOptions, T> config)
    {
        _referenceInputConfigs.Add(config);
        return this;
    }

    public TransactionTemplateBuilder<T> AddOutput(Action<OutputOptions, T> config)
    {
        _outputConfigs.Add(config);
        return this;
    }

    public TransactionTemplateBuilder<T> AddMint(Action<MintOptions<T>, T> config)
    {
        _mintConfigs.Add(config);
        return this;
    }

    public TransactionTemplateBuilder<T> AddWithdrawal(Action<WithdrawalOptions<T>, T> config)
    {
        _withdrawalConfigs.Add(config);
        return this;
    }

    public TransactionTemplateBuilder<T> AddRequiredSigner(string signer)
    {
        requiredSigners.Add(signer);
        return this;
    }

    public TransactionTemplateBuilder<T> SetValidFrom(ulong slot)
    {
        _validFrom = slot;
        return this;
    }

    public TransactionTemplateBuilder<T> SetValidTo(ulong slot)
    {
        _validTo = slot;
        return this;
    }

    public TransactionTemplateBuilder<T> AddStaticParty(string partyIdent, string party, bool isChange = false)
    {
        if (isChange)
        {
            _changeAddress = partyIdent;
        }
        _staticParties[partyIdent] = party;
        return this;
    }

    public Func<T, Task<Transaction>> Build()
    {
        return async param =>
        {
            var context = new BuildContext
            {
                TxBuilder = TransactionBuilder.Create(await _provider!.GetParametersAsync())
            };
            var parties = ResolveParties(param);

            if (_changeAddress is null)
            {
                throw new InvalidOperationException("Change address not set");
            }

            WalletAddress changeAddress = WalletAddress.FromBech32(parties[_changeAddress]);

            ProcessInputs(param, context);
            ProcessMints(param, context);

            List<Value> requiredAmount = [];
            int changeIndex = 0;
            ProcessOutputs(param, context, parties, requiredAmount, ref changeIndex);

            List<ResolvedInput> utxos = await _provider!.GetUtxosAsync([parties[_changeAddress]]);
            var allUtxos = new List<ResolvedInput>(utxos);
            foreach (string address in context.InputAddresses.Distinct())
            {
                if (address != _changeAddress)
                {
                    allUtxos.AddRange(await _provider!.GetUtxosAsync([parties[address]]));
                }
            }

            List<Script> script = GetScripts(context.IsSmartContractTx, context.ReferenceInputs, allUtxos);

            ResolvedInput? feeInput = SelectFeeInput(utxos);
            if (feeInput is not null)
            {
                utxos.Remove(feeInput);
                context.TxBuilder.AddInput(feeInput.Outref);
            }

            ResolvedInput? collateralInput = SelectCollateralInput(utxos, context.IsSmartContractTx);
            if (collateralInput is not null)
            {
                utxos.Remove(collateralInput);
                context.TxBuilder.AddCollateral(collateralInput.Outref);
                context.TxBuilder.SetCollateralReturn(collateralInput.Output);
            }

            List<ResolvedInput> specifiedInputsUtxos = GetSpecifiedInputsUtxos(context.SpecifiedInputs, allUtxos);

            var coinSelectionResult = PerformCoinSelection(
                utxos,
                requiredAmount,
                specifiedInputsUtxos,
                context
            );

            foreach (var consumedInput in coinSelectionResult.Inputs)
            {
                context.TxBuilder.AddInput(consumedInput.Outref);
            }

            ulong totalLovelaceChange = coinSelectionResult.LovelaceChange;
            Dictionary<byte[], TokenBundleOutput> assetsChange = coinSelectionResult.AssetsChange;

            var lovelaceChange = new Lovelace(totalLovelaceChange + (feeInput?.Output.Amount().Lovelace() ?? 0));
            if (feeInput!.Output.Amount() is LovelaceWithMultiAsset lovelaceWithMultiAsset)
            {
                Dictionary<byte[], Dictionary<byte[], ulong>> existingAssetsChange = [];
                Dictionary<byte[], Dictionary<byte[], ulong>> feeInputAssetsChange = [];

                foreach (var asset in assetsChange)
                {
                    existingAssetsChange.Add(asset.Key, asset.Value.Value);
                }

                foreach (var asset in lovelaceWithMultiAsset.MultiAsset.Value)
                {
                    feeInputAssetsChange.Add(asset.Key, asset.Value.Value);
                }

                Dictionary<byte[], Dictionary<byte[], ulong>> combinedAssets = new(existingAssetsChange);

                foreach (var asset in feeInputAssetsChange)
                {
                    byte[]? matchingKey = null;
                    foreach (var existingKey in combinedAssets.Keys)
                    {
                        if (existingKey.SequenceEqual(asset.Key))
                        {
                            matchingKey = existingKey;
                            break;
                        }
                    }
                    
                    if (matchingKey != null)
                    {
                        var existingTokens = combinedAssets[matchingKey];

                        foreach (var token in asset.Value)
                        {
                            byte[]? matchingTokenKey = null;
                            foreach (var existingTokenKey in existingTokens.Keys)
                            {
                                if (existingTokenKey.SequenceEqual(token.Key))
                                {
                                    matchingTokenKey = existingTokenKey;
                                    break;
                                }
                            }

                            if (matchingTokenKey != null)
                            {
                                existingTokens[matchingTokenKey] += token.Value;
                            }
                            else
                            {
                                existingTokens.Add(token.Key, token.Value);
                            }
                        }
                    }
                    else
                    {
                        combinedAssets.Add(asset.Key, new Dictionary<byte[], ulong>(asset.Value));
                    }

                }


                Dictionary<byte[], TokenBundleOutput> convertedAssetsChange = [];

                foreach (var asset in combinedAssets)
                {
                    TokenBundleOutput tokenBundle = new(asset.Value);
                    convertedAssetsChange.Add(asset.Key, tokenBundle);
                }

                assetsChange = convertedAssetsChange;

            }
            Value changeValue = assetsChange.Count > 0
                ? new LovelaceWithMultiAsset(lovelaceChange, new MultiAssetOutput(assetsChange))
                : lovelaceChange;

            var changeOutput = new AlonzoTransactionOutput(new Address(changeAddress.ToBytes()), changeValue, null);
            context.TxBuilder.AddOutput(changeOutput, true);

            List<TransactionInput> sortedInputs = [.. context.TxBuilder.body.Inputs.GetValue().OrderBy(e => Convert.ToHexString(e.TransactionId)).ThenBy(e => e.Index)];
            context.TxBuilder.SetInputs(sortedInputs);

            List<TransactionInput> sortedRefInputs = [];
            if (context.TxBuilder.body.ReferenceInputs?.GetValue() != null)
            {
                sortedRefInputs = [.. context.TxBuilder.body.ReferenceInputs.GetValue()
                .OrderBy(e => Convert.ToHexString(e.TransactionId))
                .ThenBy(e => e.Index)];

                context.TxBuilder.SetReferenceInputs(sortedRefInputs);
            }

            var inputIdToOrderedIndex = GetInputIdToOrderedIndex(context.InputsById, sortedInputs);
            var intIndexedAssociations = new Dictionary<int, Dictionary<string, int>>();
            var stringIndexedAssociations = GetIndexedAssociations(context.AssociationsByInputId, inputIdToOrderedIndex);
            var refInputIdToOrderedIndex = GetRefInputIdToOrderedIndex(context.ReferenceInputsById, sortedRefInputs);

            foreach (var (inputId, data) in stringIndexedAssociations)
            {
                if (int.TryParse(inputId, out int intKey))
                {
                    intIndexedAssociations[intKey] = data.outputIndices;
                }
            }

            ProcessWithdrawals(param, context, parties, intIndexedAssociations);

            if (context.IsSmartContractTx)
            {
                var inputIdToIndex = new Dictionary<string, ulong>();
                var outputMappings = new Dictionary<string, Dictionary<string, ulong>>();

                foreach (var (inputId, input) in context.InputsById)
                {
                    for (int i = 0; i < sortedInputs.Count; i++)
                    {
                        if (Convert.ToHexString(sortedInputs[i].TransactionId) == Convert.ToHexString(input.TransactionId) &&
                            sortedInputs[i].Index == input.Index)
                        {
                            inputIdToIndex[inputId] = (ulong)i;
                            break;
                        }
                    }
                }

                foreach (var (inputId, associations) in context.AssociationsByInputId)
                {
                    if (!outputMappings.ContainsKey(inputId))
                    {
                        outputMappings[inputId] = [];
                    }

                    foreach (var (outputId, outputIndex) in associations)
                    {
                        outputMappings[inputId][outputId] = (ulong)outputIndex;
                    }
                }

                BuildRedeemers(context, param, inputIdToIndex, refInputIdToOrderedIndex, outputMappings);

                if (context.Redeemers.Count > 0)
                {
                    var redeemerMap = new RedeemerMap(context.Redeemers);
                    var serialized = CborSerializer.Serialize(redeemerMap);

                    context.TxBuilder.SetRedeemers(redeemerMap);
                }

                // @TODO: Uncomment when Evaluate is fixed
                // context.TxBuilder.Evaluate(allUtxos);
            }

            if (requiredSigners.Count > 0)
            {
                foreach (string signer in requiredSigners)
                {
                    WalletAddress address = WalletAddress.FromBech32(parties[signer]);
                    context.TxBuilder.AddRequiredSigner(address.GetPaymentKeyHash()!);
                }
            }

            if (_validFrom > 0)
                context.TxBuilder.SetValidityIntervalStart(_validFrom);

            if (_validTo > 0)
                context.TxBuilder.SetTtl(_validTo);

            return context.TxBuilder.CalculateFee(script).Build();
        }
            ;
    }

    private Dictionary<string, string> ResolveParties(T param)
    {
        Dictionary<string, (string address, bool isChange)> additionalParties = param is ITransactionParameters parameters
            ? parameters.Parties ?? []
            : [];
        return MergeParties(_staticParties, additionalParties);
    }

    private CoinSelectionResult PerformCoinSelection(
    List<ResolvedInput> utxos,
    List<Value> requiredAmount,
    List<ResolvedInput> specifiedInputsUtxos,
    BuildContext context
)
    {
        ulong requestedLovelace = 0;
        Dictionary<string, decimal> requestedAssets = [];
        foreach (var amount in requiredAmount)
        {
            requestedLovelace += amount.Lovelace();

            if (amount is LovelaceWithMultiAsset lovelaceWithMultiAsset)
            {
                ExtractAssets(lovelaceWithMultiAsset.MultiAsset, requestedAssets);
            }
        }

        ulong originalRequestedLovelace = requestedLovelace;

        ulong specifiedInputsLovelace = 0;
        Dictionary<string, decimal> specifiedInputsAssets = [];

        foreach (var utxo in specifiedInputsUtxos)
        {
            specifiedInputsLovelace += utxo.Output.Amount().Lovelace();

            if (utxo.Output.Amount() is LovelaceWithMultiAsset lovelaceWithMultiAsset)
            {
                ExtractAssets(lovelaceWithMultiAsset.MultiAsset, specifiedInputsAssets);
            }
        }

        Dictionary<string, decimal> originalSpecifiedInputsAssets = new(specifiedInputsAssets);

        requestedLovelace = requestedLovelace > specifiedInputsLovelace ? requestedLovelace - specifiedInputsLovelace : 0;

        foreach (var asset in specifiedInputsAssets)
        {
            if (requestedAssets.ContainsKey(asset.Key))
            {
                requestedAssets[asset.Key] -= asset.Value;
                if (requestedAssets[asset.Key] <= 0)
                {
                    requestedAssets.Remove(asset.Key);
                }
            }
        }

        Dictionary<string, decimal> mintedAssets = [];

        foreach (var policy in context.Mints)
        {
            foreach (var asset in policy.Value)
            {
                string assetKey = policy.Key + asset.Key;
                if (!mintedAssets.ContainsKey(assetKey))
                {
                    mintedAssets[assetKey] = asset.Value;
                }
                else
                {
                    mintedAssets[assetKey] += asset.Value;
                }
            }
        }

        foreach (var asset in mintedAssets)
        {
            if (asset.Value > 0 && requestedAssets.ContainsKey(asset.Key))
            {
                requestedAssets[asset.Key] -= asset.Value;
                if (requestedAssets[asset.Key] <= 0)
                {
                    requestedAssets.Remove(asset.Key);
                }
            }
        }

        List<Value> updatedRequiredAmounts = [];
        Lovelace updatedRequestedLovelace = new(requestedLovelace);

        if (requestedAssets.Count == 0)
        {
            updatedRequiredAmounts.Add(updatedRequestedLovelace);
        }
        else
        {
            Dictionary<byte[], TokenBundleOutput> multiAssetDict = [];

            var assetsByPolicy = requestedAssets
                .GroupBy(a => a.Key[..56])
                .ToDictionary(g => g.Key, g => g.ToList());

            foreach (var policy in assetsByPolicy)
            {
                var policyId = Convert.FromHexString(policy.Key);
                Dictionary<byte[], ulong> tokenBundle = [];

                foreach (var asset in policy.Value)
                {
                    var assetName = Convert.FromHexString(asset.Key[56..]);
                    tokenBundle[assetName] = (ulong)asset.Value;
                }

                multiAssetDict[policyId] = new TokenBundleOutput(tokenBundle);
            }

            var multiAssetOutput = new MultiAssetOutput(multiAssetDict);
            var lovelaceWithAssets = new LovelaceWithMultiAsset(updatedRequestedLovelace, multiAssetOutput);
            updatedRequiredAmounts.Add(lovelaceWithAssets);
        }

        CoinSelectionResult selection = CoinSelectionUtil.LargestFirstAlgorithm(
            utxos,
            updatedRequiredAmounts
        );

        if (specifiedInputsLovelace > originalRequestedLovelace)
        {
            ulong excessLovelace = specifiedInputsLovelace - originalRequestedLovelace;

            selection.LovelaceChange += excessLovelace;
        }

        foreach (var mintAsset in mintedAssets.Where(a => a.Value > 0))
        {
            if (!requestedAssets.ContainsKey(mintAsset.Key))
            {
                string policyId = mintAsset.Key[..56];
                string assetName = mintAsset.Key[56..];

                byte[] policyIdBytes = Convert.FromHexString(policyId);
                byte[] assetNameBytes = Convert.FromHexString(assetName);

                AddAssetToChange(selection.AssetsChange, policyIdBytes, assetNameBytes, (ulong)mintAsset.Value);
            }
        }

        foreach (var inputAsset in originalSpecifiedInputsAssets)
        {
            if (!requestedAssets.ContainsKey(inputAsset.Key))
            {
                string policyId = inputAsset.Key[..56];
                string assetName = inputAsset.Key[56..];

                byte[] policyIdBytes = Convert.FromHexString(policyId);
                byte[] assetNameBytes = Convert.FromHexString(assetName);

                if (mintedAssets.ContainsKey(inputAsset.Key) && mintedAssets[inputAsset.Key] > 0)
                {
                    continue;
                }
                AddAssetToChange(selection.AssetsChange, policyIdBytes, assetNameBytes, (ulong)inputAsset.Value);
            }
        }

        return selection;
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
                string assetKey = (policy + token.Key).ToLowerInvariant();

                if (!assetDict.ContainsKey(assetKey))
                {
                    assetDict[assetKey] = token.Value;
                }
                else if (assetDict.ContainsKey(assetKey))
                {
                    assetDict[assetKey] += token.Value;
                }
            }
        }
    }

    private void BuildRedeemers(
        BuildContext buildContext,
        T param,
        Dictionary<string, ulong> inputIdToIndex,
        Dictionary<string, ulong> refInputIdToIndex,
        Dictionary<string, Dictionary<string, ulong>> outputMappings)
    {
        var mapping = new InputOutputMapping();

        foreach (var (inputId, inputIndex) in inputIdToIndex)
        {
            mapping.AddInput(inputId, inputIndex);
        }

        foreach (var (refInputId, refInputIndex) in refInputIdToIndex)
        {
            mapping.AddReferenceInput(refInputId, refInputIndex);
        }

        foreach (var (inputId, outputsDict) in outputMappings)
        {
            foreach (var (outputId, outputIndex) in outputsDict)
            {
                mapping.AddOutput(inputId, outputId, outputIndex);
            }
        }

        foreach (var config in _inputConfigs)
        {
            var inputOptions = new InputOptions<T> { From = "", Id = null };
            config(inputOptions, param);

            if (inputOptions.Redeemer != null)
            {
                foreach (RedeemerKey key in inputOptions.Redeemer.Value.Keys)
                {
                    buildContext.Redeemers[key] = inputOptions.Redeemer.Value[key];
                }
            }

            if (inputOptions.RedeemerBuilder != null)
            {
                if (inputOptions.Id is not null)
                {
                    var inputIndex = inputIdToIndex[inputOptions.Id];
                    var redeemerObj = inputOptions.RedeemerBuilder(mapping, param);

                    if (redeemerObj != null)
                    {
                        ProcessRedeemer(buildContext, redeemerObj, inputIndex);
                    }
                }
            }
        }

        int withdrawalIndex = 0;
        foreach (var config in _withdrawalConfigs)
        {
            var withdrawalOptions = new WithdrawalOptions<T> { From = "", Amount = 0 };
            config(withdrawalOptions, param);

            if (withdrawalOptions.Redeemer != null)
            {
                foreach (RedeemerKey key in withdrawalOptions.Redeemer.Value.Keys)
                {
                    buildContext.Redeemers[key] = withdrawalOptions.Redeemer.Value[key];
                }
            }

            if (withdrawalOptions.RedeemerBuilder != null)
            {
                var redeemerObj = withdrawalOptions.RedeemerBuilder(mapping, param);

                if (redeemerObj != null)
                {
                    ProcessRedeemer(buildContext, redeemerObj, (ulong)withdrawalIndex);
                }
            }

            withdrawalIndex++;
        }

        int mintIndex = 0;
        foreach (var config in _mintConfigs)
        {
            var mintOptions = new MintOptions<T> { Policy = "", Assets = [] };
            config(mintOptions, param);

            if (mintOptions.Redeemer != null)
            {
                foreach (RedeemerKey key in mintOptions.Redeemer.Value.Keys)
                {
                    buildContext.Redeemers[key] = mintOptions.Redeemer.Value[key];
                }
            }

            if (mintOptions.RedeemerBuilder != null)
            {
                Redeemer<CborBase> redeemer = mintOptions.RedeemerBuilder(mapping, param);

                if (redeemer != null)
                {
                    ProcessRedeemer(buildContext, redeemer, (ulong)mintIndex);
                }
            }

            mintIndex++;
        }
    }

    private void ProcessRedeemer(BuildContext buildContext, object redeemerObj, ulong index)
    {
        try
        {
            dynamic redeemer = redeemerObj;
            RedeemerTag redeemerTag;
            PlutusData redeemerData;
            ExUnits redeemerExUnits;

            try
            {
                redeemerTag = redeemer.Tag;

                var dataObj = redeemer.Data;
                if (dataObj is PlutusData pd)
                {
                    redeemerData = pd;
                }
                else
                {
                    byte[] dataBytes = CborSerializer.Serialize(dataObj);
                    redeemerData = CborSerializer.Deserialize<PlutusData>(dataBytes);
                }

                redeemerExUnits = redeemer.ExUnits;

                var key = new RedeemerKey((int)redeemerTag, index);

                var value = new RedeemerValue(redeemerData, redeemerExUnits);

                buildContext.Redeemers[key] = value;
            }
            catch (Exception innerEx)
            {
                Console.WriteLine($"Error extracting redeemer properties: {innerEx.Message}");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error processing redeemer: {ex.Message}");
        }
    }
    private void ProcessInputs(T param, BuildContext context)
    {
        foreach (var config in _inputConfigs)
        {
            var inputOptions = new InputOptions<T> { From = "", Id = null };
            config(inputOptions, param);

            if (!string.IsNullOrEmpty(inputOptions.Id) && inputOptions.UtxoRef != null)
            {
                context.InputsById[inputOptions.Id] = inputOptions.UtxoRef;
                context.AssociationsByInputId[inputOptions.Id] = [];
            }

            if (inputOptions.UtxoRef is not null)
            {

                context.SpecifiedInputs.Add(inputOptions.UtxoRef);
                context.TxBuilder.AddInput(inputOptions.UtxoRef);

            }
            if (inputOptions.Redeemer is not null || inputOptions.RedeemerBuilder is not null)
            {
                context.IsSmartContractTx = true;
            }

            if (inputOptions.MinAmount is not null)
            {
                context.MinimumLovelace += inputOptions.MinAmount.Lovelace();
            }

            if (!string.IsNullOrEmpty(inputOptions.From))
            {
                context.InputAddresses.Add(inputOptions.From);
            }
        }

        foreach (var config in _referenceInputConfigs)
        {
            var referenceInputOptions = new ReferenceInputOptions { From = "", UtxoRef = null, Id = null };
            config(referenceInputOptions, param);

            if (referenceInputOptions.UtxoRef is not null)
            {
                if (!string.IsNullOrEmpty(referenceInputOptions.Id))
                {
                    context.ReferenceInputsById[referenceInputOptions.Id] = referenceInputOptions.UtxoRef;
                }
                context.InputAddresses.Add(referenceInputOptions.From);
                context.ReferenceInputs.Add(referenceInputOptions.UtxoRef);
                context.TxBuilder.AddReferenceInput(referenceInputOptions.UtxoRef);
            }
            else
            {
                throw new Exception($"Reference input not found for {referenceInputOptions.From}");
            }
        }

    }

    private void ProcessMints(T param, BuildContext context)
    {
        foreach (var config in _mintConfigs)
        {
            var mintOptions = new MintOptions<T> { Policy = "", Assets = [] };
            config(mintOptions, param);

            if (!context.Mints.ContainsKey(mintOptions.Policy))
            {
                context.Mints[mintOptions.Policy] = [];
            }

            foreach (var (assetName, amount) in mintOptions.Assets)
            {
                context.Mints[mintOptions.Policy][assetName] = (long)amount;
                context.TxBuilder.AddMint(new MultiAssetMint(new Dictionary<byte[], TokenBundleMint>
                {
                    { Convert.FromHexString(mintOptions.Policy), new TokenBundleMint(new Dictionary<byte[], long> { { Convert.FromHexString(assetName), (long)amount } }) }
                }));
            }
        }
    }

    private void ProcessOutputs(T param, BuildContext context, Dictionary<string, string> parties, List<Value> requiredAmount, ref int changeIndex)
    {
        int outputIndex = 0;
        foreach (var config in _outputConfigs)
        {
            var outputOptions = new OutputOptions { To = "", Amount = null, Datum = null };
            config(outputOptions, param);

            if (!string.IsNullOrEmpty(outputOptions.AssociatedInputId) &&
                !string.IsNullOrEmpty(outputOptions.Id) &&
                context.AssociationsByInputId.TryGetValue(outputOptions.AssociatedInputId, out var associations))
            {
                associations[outputOptions.Id] = outputIndex;
            }

            context.TxBuilder.AddOutput(outputOptions.BuildOutput(parties, context.TxBuilder.pparams?.AdaPerUTxOByte ?? 4310));
            requiredAmount.Add(outputOptions.Amount!);

            changeIndex++;
            outputIndex++;
        }
    }

    private void ProcessWithdrawals(T param, BuildContext context, Dictionary<string, string> parties, Dictionary<int, Dictionary<string, int>> indexedAssociations)
    {
        Dictionary<RewardAccount, ulong> rewards = [];
        foreach (var config in _withdrawalConfigs)
        {
            var withdrawalOptions = new WithdrawalOptions<T> { From = "", Amount = 0 };
            config(withdrawalOptions, param);

            WalletAddress withdrawalAddress = WalletAddress.FromBech32(parties[withdrawalOptions.From]);
            rewards.Add(new RewardAccount(withdrawalAddress.ToBytes()), withdrawalOptions.Amount!);
        }

        if (rewards.Count > 0)
        {
            context.TxBuilder.SetWithdrawals(new Withdrawals(rewards));
        }
    }

    private List<Script> GetScripts(bool isSmartContractTx, List<TransactionInput> referenceInputs, List<ResolvedInput> allUtxos)
    {
        if (isSmartContractTx && referenceInputs != null && referenceInputs.Any())
        {
            List<Script> scripts = [];

            foreach (var referenceInput in referenceInputs)
            {
                foreach (var utxo in allUtxos)
                {
                    if (Convert.ToHexString(utxo.Outref.TransactionId) == Convert.ToHexString(referenceInput.TransactionId) &&
                        utxo.Outref.Index == referenceInput.Index)
                    {
                        if (utxo.Output is PostAlonzoTransactionOutput postAlonzoOutput &&
                            postAlonzoOutput.ScriptRef is not null)
                        {
                            Script script = CborSerializer.Deserialize<Script>(postAlonzoOutput.ScriptRef.Value);
                            scripts.Add(script);
                        }
                    }
                }
            }

            return scripts;
        }

        return [];
    }

    private ResolvedInput? SelectFeeInput(List<ResolvedInput> utxos)
    {
        var sortedUtxos =  utxos
            .Where(e => e.Output.Amount().Lovelace() >= 5_000_000UL)
            .OrderBy(e => e.Output.Amount().Lovelace());

        return sortedUtxos.FirstOrDefault();
    }

    private ResolvedInput? SelectCollateralInput(List<ResolvedInput> utxos, bool isSmartContractTx)
    {
        if (!isSmartContractTx) return null;
        return utxos
            .Where(e => e.Output.Amount().Lovelace() >= 5_000_000UL && e.Output.Amount() is Lovelace)
            .OrderBy(e => e.Output.Amount().Lovelace())
            .FirstOrDefault();
    }

    private List<ResolvedInput> GetSpecifiedInputsUtxos(List<TransactionInput> specifiedInputs, List<ResolvedInput> allUtxos)
    {
        List<ResolvedInput> specifiedInputsUtxos = [];
        foreach (var input in specifiedInputs)
        {
            ResolvedInput specifiedInputUtxo = allUtxos.First(e =>
                Convert.ToHexString(e.Outref.TransactionId) == Convert.ToHexString(input.TransactionId) &&
                e.Outref.Index == input.Index);
            specifiedInputsUtxos.Add(specifiedInputUtxo);
        }
        return specifiedInputsUtxos;
    }

    private Dictionary<string, int> GetInputIdToOrderedIndex(Dictionary<string, TransactionInput> inputsById, List<TransactionInput> sortedInputs)
    {
        Dictionary<string, int> inputIdToOrderedIndex = [];
        foreach (var (inputId, input) in inputsById)
        {
            for (int i = 0; i < sortedInputs.Count; i++)
            {
                if (Convert.ToHexString(sortedInputs[i].TransactionId) == Convert.ToHexString(input.TransactionId) &&
                    sortedInputs[i].Index == input.Index)
                {
                    inputIdToOrderedIndex[inputId] = i;
                    break;
                }
            }
        }
        return inputIdToOrderedIndex;
    }

    private Dictionary<string, ulong> GetRefInputIdToOrderedIndex(
    Dictionary<string, TransactionInput> refInputsById,
    List<TransactionInput> sortedRefInputs)
    {
        Dictionary<string, ulong> refInputIdToOrderedIndex = [];
        foreach (var (refInputId, refInput) in refInputsById)
        {
            for (int i = 0; i < sortedRefInputs.Count; i++)
            {
                if (Convert.ToHexString(sortedRefInputs[i].TransactionId) == Convert.ToHexString(refInput.TransactionId) &&
                    sortedRefInputs[i].Index == refInput.Index)
                {
                    refInputIdToOrderedIndex[refInputId] = (ulong)i;
                    break;
                }
            }
        }
        return refInputIdToOrderedIndex;
    }

    private Dictionary<string, (ulong inputIndex, Dictionary<string, int> outputIndices)> GetIndexedAssociations(Dictionary<string, Dictionary<string, int>> associationsByInputId, Dictionary<string, int> inputIdToOrderedIndex)
    {
        Dictionary<string, (ulong inputIndex, Dictionary<string, int> outputIndices)> indexedAssociations = [];
        foreach (var (inputId, roleToOutputIndex) in associationsByInputId)
        {
            if (inputIdToOrderedIndex.TryGetValue(inputId, out int inputIndex))
            {
                indexedAssociations[inputId] = ((ulong)inputIndex, new Dictionary<string, int>(roleToOutputIndex));
            }
        }
        return indexedAssociations;
    }

    private void AddAssetToChange(
        Dictionary<byte[], TokenBundleOutput> assetsChange,
        byte[] policyId,
        byte[] assetName,
        ulong amount
    )
    {
        KeyValuePair<byte[], TokenBundleOutput>? matchingPolicy = null;
        foreach (var policy in assetsChange)
        {
            if (policy.Key.SequenceEqual(policyId))
            {
                matchingPolicy = policy;
                break;
            }
        }

        if (matchingPolicy == null)
        {
            Dictionary<byte[], ulong> tokenBundle = new()
        {
            { assetName, amount }
        };
            assetsChange[policyId] = new TokenBundleOutput(tokenBundle);
        }
        else
        {
            var tokenBundle = matchingPolicy.Value.Value.Value;

            KeyValuePair<byte[], ulong>? matchingToken = null;
            foreach (var token in tokenBundle)
            {
                if (token.Key.SequenceEqual(assetName))
                {
                    matchingToken = token;
                    break;
                }
            }

            if (matchingToken == null)
            {
                tokenBundle[assetName] = amount;
            }
            else
            {
                tokenBundle[matchingToken.Value.Key] += amount;
            }
        }
    }

    private Dictionary<string, string> MergeParties(Dictionary<string, string> staticParties, Dictionary<string, (string address, bool isChange)> dynamicParties)
    {
        var result = new Dictionary<string, string>(staticParties);
        foreach (var (key, value) in dynamicParties)
        {
            if (value.isChange)
            {
                _changeAddress = value.address;
            }
            result[key] = value.address;
        }
        return result;
    }

    private class BuildContext
    {
        public TransactionBuilder TxBuilder { get; set; } = null!;
        public bool IsSmartContractTx { get; set; }
        public List<TransactionInput> ReferenceInputs { get; set; } = [];
        public ulong MinimumLovelace { get; set; }
        public Dictionary<string, TransactionInput> InputsById { get; } = [];
        public Dictionary<string, TransactionInput> ReferenceInputsById { get; } = [];
        public Dictionary<string, Dictionary<string, int>> AssociationsByInputId { get; } = [];
        public List<string> InputAddresses { get; } = [];
        public List<TransactionInput> SpecifiedInputs { get; } = [];
        public Dictionary<RedeemerKey, RedeemerValue> Redeemers { get; } = [];
        public Dictionary<string, Dictionary<string, long>> Mints { get; } = [];
    }
}