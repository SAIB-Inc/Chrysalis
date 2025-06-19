using Chrysalis.Cbor.Extensions;
using Chrysalis.Cbor.Extensions.Cardano.Core.Common;
using Chrysalis.Cbor.Extensions.Cardano.Core.Transaction;
using Chrysalis.Cbor.Serialization;
using Chrysalis.Cbor.Types;
using Chrysalis.Cbor.Types.Cardano.Core;
using Chrysalis.Cbor.Types.Cardano.Core.Certificates;
using Chrysalis.Cbor.Types.Cardano.Core.Common;
using Chrysalis.Cbor.Types.Cardano.Core.Protocol;
using Chrysalis.Cbor.Types.Cardano.Core.Transaction;
using Chrysalis.Cbor.Types.Cardano.Core.TransactionWitness;
using Chrysalis.Tx.Extensions;
using Chrysalis.Tx.Models;
using Chrysalis.Tx.Models.Cbor;
using Chrysalis.Tx.Utils;
using Chrysalis.Wallet.Utils;
using WalletAddress = Chrysalis.Wallet.Models.Addresses.Address;

namespace Chrysalis.Tx.Builders;

public class TransactionTemplateBuilder<T>
{
    private ICardanoDataProvider? _provider;
    private string? _changeAddress;
    private readonly Dictionary<string, string> _staticParties = [];
    private readonly List<InputConfig<T>> _inputConfigs = [];
    private readonly List<ReferenceInputConfig<T>> _referenceInputConfigs = [];
    private readonly List<OutputConfig<T>> _outputConfigs = [];
    private readonly List<MintConfig<T>> _mintConfigs = [];
    private readonly List<WithdrawalConfig<T>> _withdrawalConfigs = [];
    private MetadataConfig<T>? _metadataConfig = null;
    private readonly List<PreBuildHook<T>> _preBuildHooks = [];
    private readonly List<string> requiredSigners = [];
    private NativeScriptBuilder<T>? _nativeScriptBuilder;
    private ulong _validFrom;
    private ulong _validTo;

    public static TransactionTemplateBuilder<T> Create(ICardanoDataProvider provider) => new TransactionTemplateBuilder<T>().SetProvider(provider);

    public TransactionTemplateBuilder<T> SetPreBuildHook(PreBuildHook<T> preBuildHook)
    {
        _preBuildHooks.Add(preBuildHook);
        return this;
    }
    private TransactionTemplateBuilder<T> SetProvider(ICardanoDataProvider provider)
    {
        _provider = provider;
        return this;
    }

    public TransactionTemplateBuilder<T> AddInput(InputConfig<T> config)
    {
        _inputConfigs.Add(config);
        return this;
    }


    public TransactionTemplateBuilder<T> AddReferenceInput(ReferenceInputConfig<T> config)
    {
        _referenceInputConfigs.Add(config);
        return this;
    }

    public TransactionTemplateBuilder<T> AddOutput(OutputConfig<T> config)
    {
        _outputConfigs.Add(config);
        return this;
    }

    public TransactionTemplateBuilder<T> AddMetadata(MetadataConfig<T> config)
    {
        _metadataConfig = config;
        return this;
    }

    public TransactionTemplateBuilder<T> AddMint(MintConfig<T> config)
    {
        _mintConfigs.Add(config);
        return this;
    }

    public TransactionTemplateBuilder<T> AddWithdrawal(WithdrawalConfig<T> config)
    {
        _withdrawalConfigs.Add(config);
        return this;
    }

    public TransactionTemplateBuilder<T> AddNativeScript(NativeScriptBuilder<T> nativeScriptBuilder)
    {
        _nativeScriptBuilder = nativeScriptBuilder;
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


    private async Task<Transaction> EvaluateTemplate(T param, bool eval = true, ulong fee = 0)
    {
        BuildContext context = new()
        {
            TxBuilder = TransactionBuilder.Create(await _provider!.GetParametersAsync())
        };

        Dictionary<string, string> parties = ResolveParties(param);

        if (_validFrom > 0)
            context.TxBuilder.SetValidityIntervalStart(_validFrom);

        if (_validTo > 0)
            context.TxBuilder.SetTtl(_validTo);

        WalletAddress changeAddress = WalletAddress.FromBech32(parties["change"]);

        context.AssociationsByInputId["fee"] = [];

        ProcessInputs(param, context);
        ProcessMints(param, context);

        List<Value> requiredAmount = [];
        int changeIndex = 0;
        ProcessOutputs(param, context, parties, requiredAmount, ref changeIndex, fee);

        List<ResolvedInput> utxos = await _provider!.GetUtxosAsync([parties["change"]]);

        List<ResolvedInput> allUtxos = [.. utxos];
        foreach (string address in context.InputAddresses.Distinct())
        {
            if (address != _changeAddress)
            {
                allUtxos.AddRange(await _provider!.GetUtxosAsync([parties[address]]));
            }
        }

        List<Script> scripts = GetScripts(context.IsSmartContractTx, context.ReferenceInputs, allUtxos);

        ResolvedInput? collateralInput = SelectCollateralInput(utxos, context.IsSmartContractTx);
        if (collateralInput is not null)
        {
            utxos.Remove(collateralInput);
            context.TxBuilder.AddCollateral(collateralInput.Outref);
            context.TxBuilder.SetCollateralReturn(collateralInput.Output);
        }

        List<ResolvedInput> specifiedInputsUtxos = GetSpecifiedInputsUtxos(context.SpecifiedInputs, allUtxos);
        context.ResolvedInputs.AddRange(specifiedInputsUtxos);

        CoinSelectionResult coinSelectionResult = PerformCoinSelection(
            utxos,
            requiredAmount,
            specifiedInputsUtxos,
            context
        );

        ulong totalLovelaceChange = coinSelectionResult.LovelaceChange;
        Lovelace lovelaceChange = new(totalLovelaceChange);

        Dictionary<byte[], TokenBundleOutput> assetsChange = coinSelectionResult.AssetsChange;

        foreach (ResolvedInput consumedInput in coinSelectionResult.Inputs)
        {
            context.ResolvedInputs.Add(consumedInput);
            context.TxBuilder.AddInput(consumedInput.Outref);
        }

        ResolvedInput? feeInput = SelectFeeInput(utxos, coinSelectionResult.Inputs);
        if (feeInput is not null)
        {
            utxos.Remove(feeInput);
            context.ResolvedInputs.Add(feeInput);
            context.InputsById["fee"] = feeInput.Outref;
            context.TxBuilder.AddInput(feeInput.Outref);

            lovelaceChange = new(totalLovelaceChange + (feeInput?.Output.Amount().Lovelace() ?? 0));

            if (feeInput!.Output.Amount() is LovelaceWithMultiAsset lovelaceWithMultiAsset)
            {
                // OPTIMIZED: Use ByteArrayEqualityComparer instead of SequenceEqual loops
                var feeInputAssetsChange = new Dictionary<byte[], Dictionary<byte[], ulong>>(ByteArrayEqualityComparer.Instance);
                var existingAssetsChange = new Dictionary<byte[], Dictionary<byte[], ulong>>(ByteArrayEqualityComparer.Instance);

                // Copy existing assets change
                foreach (var asset in assetsChange)
                {
                    existingAssetsChange.Add(asset.Key, asset.Value.Value);
                }

                // Copy fee input assets
                foreach (var asset in lovelaceWithMultiAsset.MultiAsset.Value)
                {
                    feeInputAssetsChange.Add(asset.Key, asset.Value.Value);
                }

                // BEFORE: This used nested loops with SequenceEqual
                // AFTER: Direct dictionary operations with custom comparer
                var combinedAssets = new Dictionary<byte[], Dictionary<byte[], ulong>>(existingAssetsChange, ByteArrayEqualityComparer.Instance);

                foreach (var (policyId, tokens) in feeInputAssetsChange)
                {
                    if (combinedAssets.TryGetValue(policyId, out var existingTokens))
                    {
                        // Merge token bundles
                        foreach (var (tokenName, amount) in tokens)
                        {
                            if (existingTokens.TryGetValue(tokenName, out var existingAmount))
                            {
                                existingTokens[tokenName] = existingAmount + amount;
                            }
                            else
                            {
                                existingTokens[tokenName] = amount;
                            }
                        }
                    }
                    else
                    {
                        // Add new policy with all its tokens
                        combinedAssets[policyId] = new Dictionary<byte[], ulong>(tokens, ByteArrayEqualityComparer.Instance);
                    }
                }

                // Convert back to TokenBundleOutput format
                var convertedAssetsChange = new Dictionary<byte[], TokenBundleOutput>(ByteArrayEqualityComparer.Instance);
                foreach (var (policyId, tokens) in combinedAssets)
                {
                    convertedAssetsChange[policyId] = new TokenBundleOutput(tokens);
                }

                assetsChange = convertedAssetsChange;
            }
        }

        Value changeValue = assetsChange.Count > 0
            ? new LovelaceWithMultiAsset(lovelaceChange, new MultiAssetOutput(assetsChange))
            : lovelaceChange;

        TransactionOutput changeOutput = new AlonzoTransactionOutput(new Address(changeAddress.ToBytes()), changeValue, null);

        if (!string.IsNullOrEmpty(_changeAddress) && lovelaceChange.Value > 0)
        {
            context.TxBuilder.AddOutput(changeOutput, true);
        }

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

        Dictionary<string, int> inputIdToOrderedIndex = TransactionTemplateBuilder<T>.GetInputIdToOrderedIndexOptimized(context.InputsById, sortedInputs);
        Dictionary<int, Dictionary<string, int>> intIndexedAssociations = [];
        Dictionary<string, (ulong inputIndex, Dictionary<string, int> outputIndices)> stringIndexedAssociations = GetIndexedAssociations(context.AssociationsByInputId, inputIdToOrderedIndex);
        Dictionary<string, ulong> refInputIdToOrderedIndex = GetRefInputIdToOrderedIndex(context.ReferenceInputsById, sortedRefInputs);

        foreach (var (inputId, data) in stringIndexedAssociations)
        {
            if (int.TryParse(inputId, out int intKey))
            {
                intIndexedAssociations[intKey] = data.outputIndices;
            }
        }

        ProcessWithdrawals(param, context, parties, intIndexedAssociations);

        if (requiredSigners.Count > 0)
        {
            foreach (string signer in requiredSigners)
            {
                WalletAddress address = WalletAddress.FromBech32(parties[signer]);
                context.TxBuilder.AddRequiredSigner(address.GetPaymentKeyHash()!);
            }
        }

        if (context.IsSmartContractTx)
        {
            var inputIdToIndex = new Dictionary<string, ulong>();
            var outputMappings = new Dictionary<string, Dictionary<string, ulong>>();

            // OPTIMIZED: Create lookup dictionary instead of nested loops
            var sortedInputsLookup = CreateInputLookupOptimized(sortedInputs);

            foreach (var (inputId, input) in context.InputsById)
            {
                var inputKey = (input.TransactionId, input.Index);
                if (sortedInputsLookup.TryGetValue(inputKey, out var index))
                {
                    inputIdToIndex[inputId] = (ulong)index;
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
                RedeemerMap redeemerMap = new(context.Redeemers);
                byte[] serialized = CborSerializer.Serialize(redeemerMap);

                context.TxBuilder.SetRedeemers(redeemerMap);
            }
        }

        if (_metadataConfig is not null)
        {
            Metadata metadata = _metadataConfig(param);
            context.TxBuilder.SetMetadata(metadata);

            PostMaryTransaction tx = context.TxBuilder.Build();
            AuxiliaryData auxData = tx.AuxiliaryData!;

            context.TxBuilder.SetAuxiliaryDataHash(HashUtil.Blake2b256(CborSerializer.Serialize(auxData)));
        }

        if (_nativeScriptBuilder is not null)
        {
            NativeScript nativeScript = _nativeScriptBuilder(param);
            context.TxBuilder.AddNativeScript(nativeScript);
        }

        foreach (PreBuildHook<T> hook in _preBuildHooks)
        {
            var mapping = new InputOutputMapping();

            foreach (var (inputId, index) in inputIdToOrderedIndex)
            {
                mapping.AddInput(inputId, (ulong)index);
            }

            foreach (var (refInputId, refInputIndex) in refInputIdToOrderedIndex)
            {
                mapping.AddReferenceInput(refInputId, refInputIndex);
            }

            foreach (var (inputId, outputsDict) in stringIndexedAssociations)
            {
                foreach (var (outputId, outputIndex) in outputsDict.outputIndices)
                {
                    mapping.AddOutput(inputId, outputId, (ulong)outputIndex);
                }
            }

            hook(context.TxBuilder, mapping, param);
        }

        if (context.IsSmartContractTx && eval)
        {
            context.TxBuilder.Evaluate(allUtxos);
        }

        return context.TxBuilder.CalculateFee(scripts, fee, 5).Build();
    }

    public TransactionTemplate<T> Build(bool Eval = true)
    {
        return async param =>
        {
            PostMaryTransaction? draftTx = await EvaluateTemplate(param, Eval) as PostMaryTransaction;
            return await EvaluateTemplate(param, Eval, draftTx?.TransactionBody.Fee() ?? 0);
        };
    }

    private Dictionary<string, string> ResolveParties(T param)
    {
        Dictionary<string, (string address, bool isChange)> additionalParties = param is ITransactionParameters parameters
            ? parameters.Parties ?? []
            : [];
        return MergeParties(_staticParties, additionalParties);
    }

    private static Dictionary<(byte[] TransactionId, ulong Index), int> CreateInputLookupOptimized(List<TransactionInput> sortedInputs)
    {
        var inputLookup = new Dictionary<(byte[], ulong), int>(new TransactionInputEqualityComparer());

        for (int i = 0; i < sortedInputs.Count; i++)
        {
            var input = sortedInputs[i];
            inputLookup[(input.TransactionId, input.Index)] = i;
        }

        return inputLookup;
    }

    private class TransactionInputEqualityComparer : IEqualityComparer<(byte[] TransactionId, ulong Index)>
    {
        public bool Equals((byte[] TransactionId, ulong Index) x, (byte[] TransactionId, ulong Index) y)
        {
            return x.Index == y.Index && ByteArrayEqualityComparer.Instance.Equals(x.TransactionId, y.TransactionId);
        }

        public int GetHashCode((byte[] TransactionId, ulong Index) obj)
        {
            return HashCode.Combine(ByteArrayEqualityComparer.Instance.GetHashCode(obj.TransactionId), obj.Index);
        }
    }

    private static CoinSelectionResult PerformCoinSelection(
        List<ResolvedInput> utxos,
        List<Value> requiredAmount,
        List<ResolvedInput> specifiedInputsUtxos,
        BuildContext context
    )
    {
        ulong requestedLovelace = 0;
        Dictionary<string, decimal> requestedAssets = [];

        foreach (Value amount in requiredAmount)
        {
            requestedLovelace += amount.Lovelace();

            if (amount is LovelaceWithMultiAsset lovelaceWithMultiAsset)
            {
                ExtractAssets(lovelaceWithMultiAsset.MultiAsset, requestedAssets);
            }
        }

        ulong originalRequestedLovelace = requestedLovelace;
        Dictionary<string, decimal> originalRequestedAssets = new(requestedAssets);

        ulong specifiedInputsLovelace = 0;
        Dictionary<string, decimal> specifiedInputsAssets = [];

        foreach (ResolvedInput utxo in specifiedInputsUtxos)
        {
            specifiedInputsLovelace += utxo.Output.Amount().Lovelace();

            if (utxo.Output.Amount() is LovelaceWithMultiAsset lovelaceWithMultiAsset)
            {
                ExtractAssets(lovelaceWithMultiAsset.MultiAsset, specifiedInputsAssets);
            }
        }

        Dictionary<string, decimal> originalSpecifiedInputsAssets = new(specifiedInputsAssets);

        Dictionary<string, decimal> mintedAssets = [];

        foreach (var policy in context.Mints)
        {
            foreach (var asset in policy.Value)
            {
                string assetKey = (policy.Key + asset.Key).ToLowerInvariant();
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

        Dictionary<string, decimal> originalMintedAssets = new(mintedAssets);


        requestedLovelace = requestedLovelace > specifiedInputsLovelace ? requestedLovelace - specifiedInputsLovelace : 0;


        foreach (var asset in mintedAssets)
        {
            if (asset.Value > 0)
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
            else
            {
                if (!requestedAssets.ContainsKey(asset.Key))
                {
                    requestedAssets[asset.Key.ToLowerInvariant()] = Math.Abs(asset.Value);
                }
                else
                {
                    requestedAssets[asset.Key] += Math.Abs(asset.Value);
                }
            }
        }

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


        foreach (var assetEntry in originalSpecifiedInputsAssets)
        {
            string assetKey = assetEntry.Key;
            decimal amount = assetEntry.Value;
            bool consumedByOutput = originalRequestedAssets.ContainsKey(assetKey);
            bool mintRelated = mintedAssets.ContainsKey(assetKey);

            if (consumedByOutput)
            {

                if (originalRequestedAssets[assetKey] < amount)
                {
                    decimal changeAmount = amount - originalRequestedAssets[assetKey];

                    string policyId = assetKey[..56];
                    string assetName = assetKey[56..];
                    byte[] policyIdBytes = Convert.FromHexString(policyId);
                    byte[] assetNameBytes = Convert.FromHexString(assetName);

                    AddAssetToChange(selection.AssetsChange, policyIdBytes, assetNameBytes, (int)changeAmount);
                }
            }
            else if (mintRelated)
            {
                if (mintedAssets[assetKey] < 0)
                {
                    continue;
                }

                string policyId = assetKey[..56];
                string assetName = assetKey[56..];
                byte[] policyIdBytes = Convert.FromHexString(policyId);
                byte[] assetNameBytes = Convert.FromHexString(assetName);

                AddAssetToChange(selection.AssetsChange, policyIdBytes, assetNameBytes, (int)amount);
            }
            else
            {
                string policyId = assetKey[..56];
                string assetName = assetKey[56..];
                byte[] policyIdBytes = Convert.FromHexString(policyId);
                byte[] assetNameBytes = Convert.FromHexString(assetName);

                AddAssetToChange(selection.AssetsChange, policyIdBytes, assetNameBytes, (int)amount);
            }
        }

        foreach (var assetEntry in mintedAssets)
        {
            string assetKey = assetEntry.Key;
            decimal amount = assetEntry.Value;

            if (amount <= 0)
                continue;

            decimal requiredExcessAmount = originalRequestedAssets.ContainsKey(assetKey) ?
                originalRequestedAssets[assetKey] : 0;

            decimal excessAmount = amount - requiredExcessAmount;

            if (excessAmount > 0)
            {
                string policyId = assetKey[..56];
                string assetName = assetKey[56..];
                byte[] policyIdBytes = Convert.FromHexString(policyId);
                byte[] assetNameBytes = Convert.FromHexString(assetName);

                AddAssetToChange(selection.AssetsChange, policyIdBytes, assetNameBytes, (int)excessAmount);
            }
        }

        foreach (var token in selection.AssetsChange.ToList())
        {
            if (token.Value.Value.Count == 0)
            {
                selection.AssetsChange.Remove(token.Key);
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
        mapping.SetResolvedInputs(buildContext.ResolvedInputs);
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
                    var redeemerObj = inputOptions.RedeemerBuilder(mapping, param, buildContext.TxBuilder);

                    if (redeemerObj != null)
                    {
                        TransactionTemplateBuilder<T>.ProcessRedeemer(buildContext, redeemerObj, inputIndex);
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
                var redeemerObj = withdrawalOptions.RedeemerBuilder(mapping, param, buildContext.TxBuilder);

                if (redeemerObj != null)
                {
                    TransactionTemplateBuilder<T>.ProcessRedeemer(buildContext, redeemerObj, (ulong)withdrawalIndex);
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
                Redeemer<CborBase> redeemer = mintOptions.RedeemerBuilder(mapping, param, buildContext.TxBuilder);

                if (redeemer != null)
                {
                    TransactionTemplateBuilder<T>.ProcessRedeemer(buildContext, redeemer, (ulong)mintIndex);
                }
            }

            mintIndex++;
        }
    }

    private static void ProcessRedeemer(BuildContext buildContext, object redeemerObj, ulong index)
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

                RedeemerKey key = new((int)redeemerTag, index);

                RedeemerValue value = new(redeemerData, redeemerExUnits);

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
            ReferenceInputOptions referenceInputOptions = new() { From = "", UtxoRef = null, Id = null };
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
        }

    }

    private void ProcessMints(T param, BuildContext context)
    {
        foreach (MintConfig<T> config in _mintConfigs)
        {
            var mintOptions = new MintOptions<T> { Policy = "", Assets = [] };
            config(mintOptions, param);

            if (!context.Mints.TryGetValue(mintOptions.Policy, out Dictionary<string, long>? value))
            {
                value = [];
                context.Mints[mintOptions.Policy] = value;
            }
            if (mintOptions.RedeemerBuilder != null || mintOptions.Redeemer is not null)
            {
                context.IsSmartContractTx = true;
            }

            foreach (var (assetName, amount) in mintOptions.Assets)
            {
                value[assetName] = amount;
                context.TxBuilder.AddMint(new MultiAssetMint(new Dictionary<byte[], TokenBundleMint>
                {
                    { Convert.FromHexString(mintOptions.Policy), new TokenBundleMint(new Dictionary<byte[], long> { { Convert.FromHexString(assetName), (long)amount } }) }
                }));
            }
        }
    }

    private void ProcessOutputs(T param, BuildContext context, Dictionary<string, string> parties, List<Value> requiredAmount, ref int changeIndex, ulong fee)
    {
        int outputIndex = 0;
        foreach (OutputConfig<T> config in _outputConfigs)
        {
            OutputOptions outputOptions = new() { To = "", Amount = null, Datum = null };
            config(outputOptions, param, fee);
            if (
                !string.IsNullOrEmpty(outputOptions.AssociatedInputId) &&
                !string.IsNullOrEmpty(outputOptions.Id) &&
                context.AssociationsByInputId.TryGetValue(outputOptions.AssociatedInputId, out var associations)
            )
            {
                associations[outputOptions.Id] = outputIndex;
            }

            TransactionOutput output = outputOptions.BuildOutput(parties, context.TxBuilder.pparams?.AdaPerUTxOByte ?? 4310);

            context.TxBuilder.AddOutput(output);
            requiredAmount.Add(output.Amount());

            changeIndex++;
            outputIndex++;
        }
    }

    private void ProcessWithdrawals(T param, BuildContext context, Dictionary<string, string> parties, Dictionary<int, Dictionary<string, int>> indexedAssociations)
    {
        Dictionary<RewardAccount, ulong> rewards = [];
        foreach (WithdrawalConfig<T> config in _withdrawalConfigs)
        {
            WithdrawalOptions<T> withdrawalOptions = new() { From = "", Amount = 0 };
            config(withdrawalOptions, param);
            if (withdrawalOptions.From != string.Empty)
            {
                WalletAddress withdrawalAddress = WalletAddress.FromBech32(parties[withdrawalOptions.From]);
                rewards.Add(new RewardAccount(withdrawalAddress.ToBytes()), withdrawalOptions.Amount!);
            }
        }

        if (rewards.Count > 0)
        {
            context.TxBuilder.SetWithdrawals(new Withdrawals(rewards));
        }
    }

    private static List<Script> GetScripts(bool isSmartContractTx, List<TransactionInput> referenceInputs, List<ResolvedInput> allUtxos)
    {
        if (!isSmartContractTx || referenceInputs == null || !referenceInputs.Any())
            return [];

        var scripts = new List<Script>();

        // OPTIMIZED: Create lookup dictionary for faster matching
        var utxoLookup = new Dictionary<(byte[], ulong), ResolvedInput>(new TransactionInputEqualityComparer());

        foreach (var utxo in allUtxos)
        {
            utxoLookup[(utxo.Outref.TransactionId, utxo.Outref.Index)] = utxo;
        }

        foreach (var referenceInput in referenceInputs)
        {
            var key = (referenceInput.TransactionId, referenceInput.Index);
            if (utxoLookup.TryGetValue(key, out var utxo))
            {
                if (utxo.Output is PostAlonzoTransactionOutput postAlonzoOutput &&
                    postAlonzoOutput.ScriptRef is not null)
                {
                    Script script = CborSerializer.Deserialize<Script>(postAlonzoOutput.ScriptRef.Value);
                    scripts.Add(script);
                }
            }
        }

        return scripts;
    }

    private static ResolvedInput? SelectFeeInput(List<ResolvedInput> utxos, List<ResolvedInput> consumedInputs)
    {
        // Create a lookup set for faster exclusion checks
        var consumedLookup = new HashSet<(string txId, ulong index)>();
        foreach (var input in consumedInputs)
        {
            consumedLookup.Add((Convert.ToHexString(input.Outref.TransactionId), input.Outref.Index));
        }

        return utxos
            .Where(e => e.Output.Amount().Lovelace() >= 5_000_000UL)
            .Where(e => !consumedLookup.Contains((Convert.ToHexString(e.Outref.TransactionId), e.Outref.Index)))
            .OrderBy(e => e.Output.Amount().Lovelace())
            .FirstOrDefault();
    }

    private static ResolvedInput? SelectCollateralInput(List<ResolvedInput> utxos, bool isSmartContractTx)
    {
        if (!isSmartContractTx) return null;
        return utxos
            .Where(e => e.Output.Amount().Lovelace() >= 7_000_000UL && e.Output.Amount() is Lovelace)
            .OrderBy(e => e.Output.Amount().Lovelace())
            .FirstOrDefault();
    }

    private static List<ResolvedInput> GetSpecifiedInputsUtxos(List<TransactionInput> specifiedInputs, List<ResolvedInput> allUtxos)
    {
        List<ResolvedInput> specifiedInputsUtxos = [];
        foreach (TransactionInput input in specifiedInputs)
        {
            ResolvedInput specifiedInputUtxo = allUtxos.First(e =>
                Convert.ToHexString(e.Outref.TransactionId) == Convert.ToHexString(input.TransactionId) &&
                e.Outref.Index == input.Index);
            specifiedInputsUtxos.Add(specifiedInputUtxo);
        }
        return specifiedInputsUtxos;
    }

    private static Dictionary<string, int> GetInputIdToOrderedIndexOptimized(
        Dictionary<string, TransactionInput> inputsById,
        List<TransactionInput> sortedInputs)
    {
        var inputIdToOrderedIndex = new Dictionary<string, int>(inputsById.Count);

        // Create lookup dictionary using byte array comparer for transaction IDs
        var inputLookup = CreateInputLookupOptimized(sortedInputs);

        foreach (var (inputId, input) in inputsById)
        {
            var key = (input.TransactionId, input.Index);
            if (inputLookup.TryGetValue(key, out var index))
            {
                inputIdToOrderedIndex[inputId] = index;
            }
        }

        return inputIdToOrderedIndex;
    }

    private static Dictionary<string, ulong> GetRefInputIdToOrderedIndex(
    Dictionary<string, TransactionInput> refInputsById,
    List<TransactionInput> sortedRefInputs)
    {
        var refInputIdToOrderedIndex = new Dictionary<string, ulong>();

        // OPTIMIZED: Create lookup dictionary to avoid nested loops
        var refInputLookup = new Dictionary<(byte[], ulong), int>(new TransactionInputEqualityComparer());

        for (int i = 0; i < sortedRefInputs.Count; i++)
        {
            var refInput = sortedRefInputs[i];
            refInputLookup[(refInput.TransactionId, refInput.Index)] = i;
        }

        foreach (var (refInputId, refInput) in refInputsById)
        {
            var key = (refInput.TransactionId, refInput.Index);
            if (refInputLookup.TryGetValue(key, out var index))
            {
                refInputIdToOrderedIndex[refInputId] = (ulong)index;
            }
        }

        return refInputIdToOrderedIndex;
    }

    private static Dictionary<string, (ulong inputIndex, Dictionary<string, int> outputIndices)> GetIndexedAssociations(Dictionary<string, Dictionary<string, int>> associationsByInputId, Dictionary<string, int> inputIdToOrderedIndex)
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

    // BEFORE: This method had multiple SequenceEqual calls
    private static void AddAssetToChange(
        Dictionary<byte[], TokenBundleOutput> assetsChange,
        byte[] policyId,
        byte[] assetName,
        int amount
    )
    {
        // AFTER: Use TryGetValue instead of SequenceEqual loops
        if (assetsChange.TryGetValue(policyId, out var existingPolicy))
        {
            var tokenBundle = existingPolicy.Value;

            if (tokenBundle.TryGetValue(assetName, out var existingAmount))
            {
                int newAmount = (int)existingAmount + amount;
                if (newAmount <= 0)
                {
                    tokenBundle.Remove(assetName);
                }
                else
                {
                    tokenBundle[assetName] = (ulong)newAmount;
                }
            }
            else if (amount > 0)
            {
                tokenBundle[assetName] = (ulong)amount;
            }
        }
        else if (amount > 0)
        {
            var tokenBundle = new Dictionary<byte[], ulong>(ByteArrayEqualityComparer.Instance)
            {
                [assetName] = (ulong)amount
            };
            assetsChange[policyId] = new TokenBundleOutput(tokenBundle);
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
        public List<ResolvedInput> ResolvedInputs { get; } = [];
        public List<TransactionInput> SpecifiedInputs { get; set; } = [];
        public Dictionary<RedeemerKey, RedeemerValue> Redeemers { get; } = [];
        public Dictionary<string, Dictionary<string, long>> Mints { get; } = [];
    }
}