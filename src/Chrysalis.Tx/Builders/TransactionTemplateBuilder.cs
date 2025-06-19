using System.Text;
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
    private static readonly Lock _builderLock = new();


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

        (List<ResolvedInput> utxos, List<ResolvedInput> allUtxos) = await GetAllUtxos(parties, context);

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
                Dictionary<byte[], Dictionary<byte[], ulong>> feeInputAssetsChange = new(ByteArrayEqualityComparer.Instance);
                Dictionary<byte[], Dictionary<byte[], ulong>> existingAssetsChange = new(ByteArrayEqualityComparer.Instance);

                // Copy existing assets change
                foreach (KeyValuePair<byte[], TokenBundleOutput> asset in assetsChange)
                {
                    existingAssetsChange.Add(asset.Key, asset.Value.Value);
                }

                // Copy fee input assets
                foreach (KeyValuePair<byte[], TokenBundleOutput> asset in lovelaceWithMultiAsset.MultiAsset.Value)
                {
                    feeInputAssetsChange.Add(asset.Key, asset.Value.Value);
                }

                // BEFORE: This used nested loops with SequenceEqual
                // AFTER: Direct dictionary operations with custom comparer
                Dictionary<byte[], Dictionary<byte[], ulong>> combinedAssets = new(existingAssetsChange, ByteArrayEqualityComparer.Instance);

                foreach ((byte[] policyId, Dictionary<byte[], ulong> tokens) in feeInputAssetsChange)
                {
                    if (combinedAssets.TryGetValue(policyId, out Dictionary<byte[], ulong>? existingTokens))
                    {
                        // Merge token bundles
                        foreach ((byte[] tokenName, ulong amount) in tokens)
                        {
                            if (existingTokens.TryGetValue(tokenName, out ulong existingAmount))
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
                Dictionary<byte[], TokenBundleOutput> convertedAssetsChange = new(ByteArrayEqualityComparer.Instance);
                foreach ((byte[] policyId, Dictionary<byte[], ulong> tokens) in combinedAssets)
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

        List<TransactionInput> sortedInputs = [.. context.TxBuilder.body.Inputs.GetValue()
            .OrderBy(e => HexStringCache.ToHexString(e.TransactionId))
            .ThenBy(e => e.Index)];
        context.TxBuilder.SetInputs(sortedInputs);

        List<TransactionInput> sortedRefInputs = [];
        if (context.TxBuilder.body.ReferenceInputs?.GetValue() != null)
        {
            sortedRefInputs = [.. context.TxBuilder.body.ReferenceInputs.GetValue()
                .OrderBy(e => HexStringCache.ToHexString(e.TransactionId))
                .ThenBy(e => e.Index)];

            context.TxBuilder.SetReferenceInputs(sortedRefInputs);
        }

        Dictionary<string, int> inputIdToOrderedIndex = TransactionTemplateBuilder<T>.GetInputIdToOrderedIndex(context.InputsById, sortedInputs);
        Dictionary<int, Dictionary<string, int>> intIndexedAssociations = [];
        Dictionary<string, (ulong inputIndex, Dictionary<string, int> outputIndices)> stringIndexedAssociations = GetIndexedAssociations(context.AssociationsByInputId, inputIdToOrderedIndex);
        Dictionary<string, ulong> refInputIdToOrderedIndex = GetRefInputIdToOrderedIndex(context.ReferenceInputsById, sortedRefInputs);

        foreach ((string inputId, (ulong inputIndex, Dictionary<string, int> outputIndices) data) in stringIndexedAssociations)
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
            Dictionary<string, ulong> inputIdToIndex = [];
            Dictionary<string, Dictionary<string, ulong>> outputMappings = [];

            // OPTIMIZED: Create lookup dictionary instead of nested loops
            Dictionary<(byte[] TransactionId, ulong Index), int> sortedInputsLookup = CreateInputLookup(sortedInputs);

            foreach ((string inputId, TransactionInput input) in context.InputsById)
            {
                (byte[] TransactionId, ulong Index) inputKey = (input.TransactionId, input.Index);
                if (sortedInputsLookup.TryGetValue(inputKey, out int index))
                {
                    inputIdToIndex[inputId] = (ulong)index;
                }
            }

            foreach ((string inputId, Dictionary<string, int> associations) in context.AssociationsByInputId)
            {
                if (!outputMappings.TryGetValue(inputId, out Dictionary<string, ulong>? value))
                {
                    value = [];
                    outputMappings[inputId] = value;
                }

                foreach ((string outputId, int outputIndex) in associations)
                {
                    value[outputId] = (ulong)outputIndex;
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
            InputOutputMapping mapping = new();

            foreach ((string inputId, int index) in inputIdToOrderedIndex)
            {
                mapping.AddInput(inputId, (ulong)index);
            }

            foreach ((string refInputId, ulong refInputIndex) in refInputIdToOrderedIndex)
            {
                mapping.AddReferenceInput(refInputId, refInputIndex);
            }

            foreach ((string inputId, (ulong inputIndex, Dictionary<string, int> outputIndices) outputsDict) in stringIndexedAssociations)
            {
                foreach ((string outputId, int outputIndex) in outputsDict.outputIndices)
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

    private static Dictionary<(byte[] TransactionId, ulong Index), int> CreateInputLookup(List<TransactionInput> sortedInputs)
    {
        Dictionary<(byte[], ulong), int> inputLookup = new(new TransactionInputEqualityComparer());

        for (int i = 0; i < sortedInputs.Count; i++)
        {
            TransactionInput input = sortedInputs[i];
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
        RequirementsResult requirements = CalculateRequirements(requiredAmount, specifiedInputsUtxos, context.Mints);

        // Step 2: Perform coin selection
        CoinSelectionResult selection = CoinSelectionUtil.LargestFirstAlgorithm(utxos, requirements.RequiredAmounts);

        // Step 3: Calculate change
        CalculateChange(selection, requirements);

        return selection;
    }

    private static RequirementsResult CalculateRequirements(
        List<Value> requiredAmount,
        List<ResolvedInput> specifiedInputsUtxos,
        Dictionary<string, Dictionary<string, long>> mints)
    {
        // FIX: Explicitly cast to decimal to resolve ambiguity
        decimal requestedLovelace = requiredAmount.Sum(amount => (decimal)amount.Lovelace());
        Dictionary<string, decimal> requestedAssets = [];

        // Extract requested assets
        foreach (Value amount in requiredAmount)
        {
            if (amount is LovelaceWithMultiAsset lovelaceWithMultiAsset)
            {
                ExtractAssets(lovelaceWithMultiAsset.MultiAsset, requestedAssets);
            }
        }

        // Process specified inputs - FIX: Explicitly cast to decimal
        decimal specifiedInputsLovelace = specifiedInputsUtxos.Sum(utxo => (decimal)utxo.Output.Amount().Lovelace());
        Dictionary<string, decimal> specifiedInputsAssets = [];

        foreach (ResolvedInput utxo in specifiedInputsUtxos)
        {
            if (utxo.Output.Amount() is LovelaceWithMultiAsset lovelaceWithMultiAsset)
            {
                ExtractAssets(lovelaceWithMultiAsset.MultiAsset, specifiedInputsAssets);
            }
        }

        // Process minted assets
        Dictionary<string, decimal> mintedAssets = [];
        foreach ((string policyId, Dictionary<string, long> assets) in mints)
        {
            foreach ((string assetName, long amount) in assets)
            {
                string assetKey = BuildAssetKey(policyId, assetName);
                mintedAssets[assetKey] = mintedAssets.GetValueOrDefault(assetKey, 0) + amount;
            }
        }

        // Adjust requirements based on specified inputs and mints
        long adjustedLovelace = Math.Max(0, (long)(requestedLovelace - specifiedInputsLovelace));
        Dictionary<string, decimal> adjustedAssets = AdjustAssetsForMintsAndInputs(requestedAssets, mintedAssets, specifiedInputsAssets);

        // Build required amounts for coin selection
        List<Value> requiredAmounts = BuildRequiredAmounts(adjustedLovelace, adjustedAssets);

        return new RequirementsResult
        {
            OriginalRequestedLovelace = (ulong)requestedLovelace,
            OriginalRequestedAssets = new Dictionary<string, decimal>(requestedAssets),
            OriginalSpecifiedInputsAssets = specifiedInputsAssets,
            OriginalMintedAssets = mintedAssets,
            RequiredAmounts = requiredAmounts,
            SpecifiedInputsLovelace = (ulong)specifiedInputsLovelace
        };
    }

    private static void ExtractAssets(MultiAssetOutput? multiAsset, Dictionary<string, decimal> assetDict)
    {
        if (multiAsset?.Value == null) return;

        foreach ((byte[] policyId, TokenBundleOutput tokenBundle) in multiAsset.Value)
        {
            string policyHex = HexStringCache.ToHexString(policyId);

            foreach ((byte[] assetName, ulong amount) in tokenBundle.Value)
            {
                string assetHex = HexStringCache.ToHexString(assetName);
                string assetKey = BuildAssetKey(policyHex, assetHex);

                assetDict[assetKey] = assetDict.GetValueOrDefault(assetKey, 0) + amount;
            }
        }
    }

    private static string BuildAssetKey(string policyId, string assetName)
    {
        lock (_builderLock) // For thread safety - use ThreadLocal<StringBuilder> for better performance
        {
            int capacity = policyId.Length + assetName.Length;
            StringBuilder builder = new(capacity);
            builder.Append(policyId);
            builder.Append(assetName);
            return builder.ToString().ToLowerInvariant();
        }
    }

    private static Dictionary<string, decimal> AdjustAssetsForMintsAndInputs(
    Dictionary<string, decimal> requestedAssets,
    Dictionary<string, decimal> mintedAssets,
    Dictionary<string, decimal> specifiedInputsAssets)
    {
        Dictionary<string, decimal> adjusted = new(requestedAssets);

        // Subtract minted assets
        foreach ((string assetKey, decimal mintAmount) in mintedAssets)
        {
            if (mintAmount > 0)
            {
                if (adjusted.TryGetValue(assetKey, out decimal requested))
                {
                    adjusted[assetKey] = Math.Max(0, requested - mintAmount);
                    if (adjusted[assetKey] <= 0)
                        adjusted.Remove(assetKey);
                }
            }
            else
            {
                // Burning - add to requirements
                adjusted[assetKey] = adjusted.GetValueOrDefault(assetKey, 0) + Math.Abs(mintAmount);
            }
        }

        // Subtract specified input assets
        foreach ((string assetKey, decimal inputAmount) in specifiedInputsAssets)
        {
            if (adjusted.TryGetValue(assetKey, out decimal required))
            {
                adjusted[assetKey] = Math.Max(0, required - inputAmount);
                if (adjusted[assetKey] <= 0)
                    adjusted.Remove(assetKey);
            }
        }

        return adjusted;
    }

    private static List<Value> BuildRequiredAmounts(long adjustedLovelace, Dictionary<string, decimal> adjustedAssets)
    {
        List<Value> requiredAmounts = [];
        Lovelace lovelace = new((ulong)Math.Max(0, adjustedLovelace));

        if (adjustedAssets.Count == 0)
        {
            requiredAmounts.Add(lovelace);
        }
        else
        {
            Dictionary<byte[], TokenBundleOutput> multiAssetDict = new(ByteArrayEqualityComparer.Instance);

            Dictionary<string, List<KeyValuePair<string, decimal>>> assetsByPolicy = adjustedAssets
                .Where(a => a.Value > 0)
                .GroupBy(a => a.Key[..56])
                .ToDictionary(g => g.Key, g => g.ToList());

            foreach ((string policyIdHex, List<KeyValuePair<string, decimal>> assets) in assetsByPolicy)
            {
                byte[] policyId = HexStringCache.FromHexString(policyIdHex);
                Dictionary<byte[], ulong> tokenBundle = new(ByteArrayEqualityComparer.Instance);

                foreach (KeyValuePair<string, decimal> asset in assets)
                {
                    byte[] assetName = HexStringCache.FromHexString(asset.Key[56..]);
                    tokenBundle[assetName] = (ulong)asset.Value;
                }

                multiAssetDict[policyId] = new TokenBundleOutput(tokenBundle);
            }

            MultiAssetOutput multiAssetOutput = new(multiAssetDict);
            LovelaceWithMultiAsset lovelaceWithAssets = new(lovelace, multiAssetOutput);
            requiredAmounts.Add(lovelaceWithAssets);
        }

        return requiredAmounts;
    }
    private static void CalculateChange(CoinSelectionResult selection, RequirementsResult requirements)
    {
        // Handle excess lovelace
        if (requirements.SpecifiedInputsLovelace > requirements.OriginalRequestedLovelace)
        {
            selection.LovelaceChange += requirements.SpecifiedInputsLovelace - requirements.OriginalRequestedLovelace;
        }

        // Handle asset change - simplified logic
        foreach ((string assetKey, decimal amount) in requirements.OriginalSpecifiedInputsAssets)
        {
            decimal consumedByOutput = requirements.OriginalRequestedAssets.GetValueOrDefault(assetKey, 0);
            decimal mintAmount = requirements.OriginalMintedAssets.GetValueOrDefault(assetKey, 0);

            if (consumedByOutput < amount && mintAmount >= 0)
            {
                decimal changeAmount = amount - consumedByOutput;
                if (changeAmount > 0)
                {
                    byte[] policyId = HexStringCache.FromHexString(assetKey[..56]);
                    byte[] assetName = HexStringCache.FromHexString(assetKey[56..]);
                    AddAssetToChange(selection.AssetsChange, policyId, assetName, (int)changeAmount);
                }
            }
        }

        // Handle mint excess
        foreach ((string assetKey, decimal amount) in requirements.OriginalMintedAssets.Where(a => a.Value > 0))
        {
            decimal requiredAmount = requirements.OriginalRequestedAssets.GetValueOrDefault(assetKey, 0);
            decimal excessAmount = amount - requiredAmount;

            if (excessAmount > 0)
            {
                byte[] policyId = HexStringCache.FromHexString(assetKey[..56]);
                byte[] assetName = HexStringCache.FromHexString(assetKey[56..]);
                AddAssetToChange(selection.AssetsChange, policyId, assetName, (int)excessAmount);
            }
        }
    }

    // Supporting record type
    private record RequirementsResult
    {
        public required ulong OriginalRequestedLovelace { get; init; }
        public required Dictionary<string, decimal> OriginalRequestedAssets { get; init; }
        public required Dictionary<string, decimal> OriginalSpecifiedInputsAssets { get; init; }
        public required Dictionary<string, decimal> OriginalMintedAssets { get; init; }
        public required List<Value> RequiredAmounts { get; init; }
        public required ulong SpecifiedInputsLovelace { get; init; }
    }

    private async Task<(List<ResolvedInput> changeUtxos, List<ResolvedInput> allUtxos)> GetAllUtxos(
    Dictionary<string, string> parties,
    BuildContext context)
    {
        // Collect all unique addresses that need UTxO fetching
        HashSet<string> addressesToFetch = [parties["change"]];

        foreach (string address in context.InputAddresses.Distinct())
        {
            if (address != _changeAddress && parties.TryGetValue(address, out string? resolvedAddress))
            {
                addressesToFetch.Add(resolvedAddress);
            }
        }

        // Single batched call instead of multiple sequential calls
        List<ResolvedInput> allUtxos = await _provider!.GetUtxosAsync(addressesToFetch.ToList());

        // Separate change UTxOs for specific processing
        string changeAddress = parties["change"];
        List<ResolvedInput> changeUtxos = allUtxos.Where(utxo =>
        {
            // You may need to adjust this based on how your UTxOs store address info
            // This is a simplified version - adapt based on your UTxO structure
            string outputAddress = GetAddressFromOutput(utxo.Output);
            return outputAddress == changeAddress;
        }).ToList();

        return (changeUtxos, allUtxos);
    }

    private static string GetAddressFromOutput(TransactionOutput output)
    {
        return output switch
        {
            AlonzoTransactionOutput alonzo => WalletAddress.FromBytes(alonzo.Address.Value).ToBech32(),
            PostAlonzoTransactionOutput postAlonzo => WalletAddress.FromBytes(postAlonzo.Address!.Value).ToBech32(),
            _ => throw new InvalidOperationException("Unknown output type")
        };
    }

    private void BuildRedeemers(
        BuildContext buildContext,
        T param,
        Dictionary<string, ulong> inputIdToIndex,
        Dictionary<string, ulong> refInputIdToIndex,
        Dictionary<string, Dictionary<string, ulong>> outputMappings)
    {
        InputOutputMapping mapping = new();
        mapping.SetResolvedInputs(buildContext.ResolvedInputs);
        foreach ((string inputId, ulong inputIndex) in inputIdToIndex)
        {
            mapping.AddInput(inputId, inputIndex);
        }

        foreach ((string refInputId, ulong refInputIndex) in refInputIdToIndex)
        {
            mapping.AddReferenceInput(refInputId, refInputIndex);
        }

        foreach ((string inputId, Dictionary<string, ulong> outputsDict) in outputMappings)
        {
            foreach ((string outputId, ulong outputIndex) in outputsDict)
            {
                mapping.AddOutput(inputId, outputId, outputIndex);
            }
        }

        foreach (InputConfig<T> config in _inputConfigs)
        {
            InputOptions<T> inputOptions = new() { From = "", Id = null };
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
                    ulong inputIndex = inputIdToIndex[inputOptions.Id];
                    Redeemer<CborBase> redeemerObj = inputOptions.RedeemerBuilder(mapping, param, buildContext.TxBuilder);

                    if (redeemerObj != null)
                    {
                        TransactionTemplateBuilder<T>.ProcessRedeemer(buildContext, redeemerObj, inputIndex);
                    }
                }
            }
        }

        int withdrawalIndex = 0;
        foreach (WithdrawalConfig<T> config in _withdrawalConfigs)
        {
            WithdrawalOptions<T> withdrawalOptions = new() { From = "", Amount = 0 };
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
                Redeemer<CborBase> redeemerObj = withdrawalOptions.RedeemerBuilder(mapping, param, buildContext.TxBuilder);

                if (redeemerObj != null)
                {
                    TransactionTemplateBuilder<T>.ProcessRedeemer(buildContext, redeemerObj, (ulong)withdrawalIndex);
                }
            }

            withdrawalIndex++;
        }

        int mintIndex = 0;
        foreach (MintConfig<T> config in _mintConfigs)
        {
            MintOptions<T> mintOptions = new() { Policy = "", Assets = [] };
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

                dynamic dataObj = redeemer.Data;
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
        foreach (InputConfig<T> config in _inputConfigs)
        {
            InputOptions<T> inputOptions = new() { From = "", Id = null };
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

        foreach (ReferenceInputConfig<T> config in _referenceInputConfigs)
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
            MintOptions<T> mintOptions = new() { Policy = "", Assets = [] };
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

            foreach ((string assetName, int amount) in mintOptions.Assets)
            {
                value[assetName] = amount;
                context.TxBuilder.AddMint(new MultiAssetMint(new Dictionary<byte[], TokenBundleMint>(ByteArrayEqualityComparer.Instance)
                {
                    { HexStringCache.FromHexString(mintOptions.Policy), new TokenBundleMint(new Dictionary<byte[], long>(ByteArrayEqualityComparer.Instance)
                    { { HexStringCache.FromHexString(assetName), amount } }) }
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
                context.AssociationsByInputId.TryGetValue(outputOptions.AssociatedInputId, out Dictionary<string, int>? associations)
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

        List<Script> scripts = [];

        // OPTIMIZED: Create lookup dictionary for faster matching
        Dictionary<(byte[], ulong), ResolvedInput> utxoLookup = new(new TransactionInputEqualityComparer());

        foreach (ResolvedInput utxo in allUtxos)
        {
            utxoLookup[(utxo.Outref.TransactionId, utxo.Outref.Index)] = utxo;
        }

        foreach (TransactionInput referenceInput in referenceInputs)
        {
            (byte[] TransactionId, ulong Index) key = (referenceInput.TransactionId, referenceInput.Index);
            if (utxoLookup.TryGetValue(key, out ResolvedInput? utxo))
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
        // OPTIMIZED: Use byte array based lookup instead of hex string conversion
        HashSet<(byte[], ulong)> consumedLookup = new(new TransactionInputEqualityComparer());

        foreach (ResolvedInput input in consumedInputs)
        {
            consumedLookup.Add((input.Outref.TransactionId, input.Outref.Index));
        }

        return utxos
            .Where(e => e.Output.Amount().Lovelace() >= 5_000_000UL)
            .Where(e => !consumedLookup.Contains((e.Outref.TransactionId, e.Outref.Index)))
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
        // OPTIMIZED: Create lookup dictionary for O(1) access instead of O(n) per input
        Dictionary<(byte[], ulong), ResolvedInput> utxoLookup = new(new TransactionInputEqualityComparer());

        foreach (ResolvedInput utxo in allUtxos)
        {
            utxoLookup[(utxo.Outref.TransactionId, utxo.Outref.Index)] = utxo;
        }

        List<ResolvedInput> specifiedInputsUtxos = new(specifiedInputs.Count);

        foreach (TransactionInput input in specifiedInputs)
        {
            (byte[] TransactionId, ulong Index) key = (input.TransactionId, input.Index);
            if (utxoLookup.TryGetValue(key, out ResolvedInput? utxo))
            {
                specifiedInputsUtxos.Add(utxo);
            }
        }

        return specifiedInputsUtxos;
    }

    private static Dictionary<string, int> GetInputIdToOrderedIndex(
        Dictionary<string, TransactionInput> inputsById,
        List<TransactionInput> sortedInputs)
    {
        Dictionary<string, int> inputIdToOrderedIndex = new(inputsById.Count);

        // Create lookup dictionary using byte array comparer for transaction IDs
        Dictionary<(byte[] TransactionId, ulong Index), int> inputLookup = CreateInputLookup(sortedInputs);

        foreach ((string inputId, TransactionInput input) in inputsById)
        {
            (byte[] TransactionId, ulong Index) key = (input.TransactionId, input.Index);
            if (inputLookup.TryGetValue(key, out int index))
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
        Dictionary<string, ulong> refInputIdToOrderedIndex = [];

        // OPTIMIZED: Create lookup dictionary to avoid nested loops
        Dictionary<(byte[], ulong), int> refInputLookup = new(new TransactionInputEqualityComparer());

        for (int i = 0; i < sortedRefInputs.Count; i++)
        {
            TransactionInput refInput = sortedRefInputs[i];
            refInputLookup[(refInput.TransactionId, refInput.Index)] = i;
        }

        foreach ((string refInputId, TransactionInput refInput) in refInputsById)
        {
            (byte[] TransactionId, ulong Index) key = (refInput.TransactionId, refInput.Index);
            if (refInputLookup.TryGetValue(key, out int index))
            {
                refInputIdToOrderedIndex[refInputId] = (ulong)index;
            }
        }

        return refInputIdToOrderedIndex;
    }

    private static Dictionary<string, (ulong inputIndex, Dictionary<string, int> outputIndices)> GetIndexedAssociations(Dictionary<string, Dictionary<string, int>> associationsByInputId, Dictionary<string, int> inputIdToOrderedIndex)
    {
        Dictionary<string, (ulong inputIndex, Dictionary<string, int> outputIndices)> indexedAssociations = [];
        foreach ((string inputId, Dictionary<string, int> roleToOutputIndex) in associationsByInputId)
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
        if (assetsChange.TryGetValue(policyId, out TokenBundleOutput? existingPolicy))
        {
            Dictionary<byte[], ulong> tokenBundle = existingPolicy.Value;

            if (tokenBundle.TryGetValue(assetName, out ulong existingAmount))
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
            Dictionary<byte[], ulong> tokenBundle = new(ByteArrayEqualityComparer.Instance)
            {
                [assetName] = (ulong)amount
            };
            assetsChange[policyId] = new TokenBundleOutput(tokenBundle);
        }
    }

    private Dictionary<string, string> MergeParties(Dictionary<string, string> staticParties, Dictionary<string, (string address, bool isChange)> dynamicParties)
    {
        Dictionary<string, string> result = new(staticParties);
        foreach ((string key, (string address, bool isChange) value) in dynamicParties)
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