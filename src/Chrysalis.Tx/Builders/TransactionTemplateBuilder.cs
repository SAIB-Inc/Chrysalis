using System.Text;
using Chrysalis.Cbor.Extensions;
using Chrysalis.Cbor.Extensions.Cardano.Core.Common;
using Chrysalis.Cbor.Extensions.Cardano.Core.Transaction;
using Chrysalis.Cbor.Serialization;
using Chrysalis.Cbor.Serialization.Utils;
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
using Chrysalis.Wallet.Models.Enums;
using Chrysalis.Wallet.Utils;
using WalletAddress = Chrysalis.Wallet.Models.Addresses.Address;

namespace Chrysalis.Tx.Builders;

/// <summary>
/// Non-generic factory for creating TransactionTemplateBuilder instances.
/// </summary>
public static class TransactionTemplateBuilder
{
    /// <summary>
    /// Creates a new template builder with the given data provider.
    /// </summary>
    /// <typeparam name="T">The transaction parameter type.</typeparam>
    /// <param name="provider">The Cardano data provider.</param>
    /// <returns>A new template builder instance.</returns>
    public static TransactionTemplateBuilder<T> Create<T>(ICardanoDataProvider provider)
    {
        ArgumentNullException.ThrowIfNull(provider);
        return TransactionTemplateBuilder<T>.CreateInternal(provider);
    }
}

/// <summary>
/// Template-based transaction builder that simplifies common transaction patterns.
/// </summary>
/// <typeparam name="T">The transaction parameter type.</typeparam>
public sealed class TransactionTemplateBuilder<T>
{
    private ICardanoDataProvider? _provider;
    private string? _changeAddress;
    private readonly Dictionary<string, string> _staticParties = [];
    private readonly List<InputConfig<T>> _inputConfigs = [];
    private readonly List<ReferenceInputConfig<T>> _referenceInputConfigs = [];
    private readonly List<OutputConfig<T>> _outputConfigs = [];
    private readonly List<MintConfig<T>> _mintConfigs = [];
    private readonly List<WithdrawalConfig<T>> _withdrawalConfigs = [];
    private MetadataConfig<T>? _metadataConfig;
    private readonly List<PreBuildHook<T>> _preBuildHooks = [];
    private readonly List<string> _requiredSigners = [];
    private NativeScriptBuilder<T>? _nativeScriptBuilder;
    private ulong _validFrom;
    private ulong _validTo;
    private static readonly Lock BuilderLock = new();

    /// <summary>
    /// Internal factory method called by the non-generic companion class.
    /// </summary>
    /// <param name="provider">The Cardano data provider.</param>
    /// <returns>A new template builder instance.</returns>
    internal static TransactionTemplateBuilder<T> CreateInternal(ICardanoDataProvider provider)
    {
        return new TransactionTemplateBuilder<T>().SetProvider(provider);
    }

    /// <summary>
    /// Adds a pre-build hook for custom transaction modifications.
    /// </summary>
    /// <param name="preBuildHook">The hook to execute before building.</param>
    /// <returns>This builder for chaining.</returns>
    public TransactionTemplateBuilder<T> SetPreBuildHook(PreBuildHook<T> preBuildHook)
    {
        ArgumentNullException.ThrowIfNull(preBuildHook);
        _preBuildHooks.Add(preBuildHook);
        return this;
    }

    private TransactionTemplateBuilder<T> SetProvider(ICardanoDataProvider provider)
    {
        _provider = provider;
        return this;
    }

    /// <summary>
    /// Adds an input configuration to the template.
    /// </summary>
    /// <param name="config">The input configuration delegate.</param>
    /// <returns>This builder for chaining.</returns>
    public TransactionTemplateBuilder<T> AddInput(InputConfig<T> config)
    {
        ArgumentNullException.ThrowIfNull(config);
        _inputConfigs.Add(config);
        return this;
    }

    /// <summary>
    /// Adds a reference input configuration to the template.
    /// </summary>
    /// <param name="config">The reference input configuration delegate.</param>
    /// <returns>This builder for chaining.</returns>
    public TransactionTemplateBuilder<T> AddReferenceInput(ReferenceInputConfig<T> config)
    {
        ArgumentNullException.ThrowIfNull(config);
        _referenceInputConfigs.Add(config);
        return this;
    }

    /// <summary>
    /// Adds an output configuration to the template.
    /// </summary>
    /// <param name="config">The output configuration delegate.</param>
    /// <returns>This builder for chaining.</returns>
    public TransactionTemplateBuilder<T> AddOutput(OutputConfig<T> config)
    {
        ArgumentNullException.ThrowIfNull(config);
        _outputConfigs.Add(config);
        return this;
    }

    /// <summary>
    /// Adds metadata configuration to the template.
    /// </summary>
    /// <param name="config">The metadata configuration delegate.</param>
    /// <returns>This builder for chaining.</returns>
    public TransactionTemplateBuilder<T> AddMetadata(MetadataConfig<T> config)
    {
        ArgumentNullException.ThrowIfNull(config);
        _metadataConfig = config;
        return this;
    }

    /// <summary>
    /// Adds a mint configuration to the template.
    /// </summary>
    /// <param name="config">The mint configuration delegate.</param>
    /// <returns>This builder for chaining.</returns>
    public TransactionTemplateBuilder<T> AddMint(MintConfig<T> config)
    {
        ArgumentNullException.ThrowIfNull(config);
        _mintConfigs.Add(config);
        return this;
    }

    /// <summary>
    /// Adds a withdrawal configuration to the template.
    /// </summary>
    /// <param name="config">The withdrawal configuration delegate.</param>
    /// <returns>This builder for chaining.</returns>
    public TransactionTemplateBuilder<T> AddWithdrawal(WithdrawalConfig<T> config)
    {
        ArgumentNullException.ThrowIfNull(config);
        _withdrawalConfigs.Add(config);
        return this;
    }

    /// <summary>
    /// Adds a native script builder to the template.
    /// </summary>
    /// <param name="nativeScriptBuilder">The native script builder delegate.</param>
    /// <returns>This builder for chaining.</returns>
    public TransactionTemplateBuilder<T> AddNativeScript(NativeScriptBuilder<T> nativeScriptBuilder)
    {
        ArgumentNullException.ThrowIfNull(nativeScriptBuilder);
        _nativeScriptBuilder = nativeScriptBuilder;
        return this;
    }

    /// <summary>
    /// Adds a required signer party identifier.
    /// </summary>
    /// <param name="signer">The party identifier for the required signer.</param>
    /// <returns>This builder for chaining.</returns>
    public TransactionTemplateBuilder<T> AddRequiredSigner(string signer)
    {
        ArgumentNullException.ThrowIfNull(signer);
        _requiredSigners.Add(signer);
        return this;
    }

    /// <summary>
    /// Sets the validity start slot.
    /// </summary>
    /// <param name="slot">The start slot number.</param>
    /// <returns>This builder for chaining.</returns>
    public TransactionTemplateBuilder<T> SetValidFrom(ulong slot)
    {
        _validFrom = slot;
        return this;
    }

    /// <summary>
    /// Sets the validity end slot (TTL).
    /// </summary>
    /// <param name="slot">The end slot number.</param>
    /// <returns>This builder for chaining.</returns>
    public TransactionTemplateBuilder<T> SetValidTo(ulong slot)
    {
        _validTo = slot;
        return this;
    }

    /// <summary>
    /// Adds a static party mapping with optional change designation.
    /// </summary>
    /// <param name="partyIdent">The party identifier.</param>
    /// <param name="party">The Bech32 address.</param>
    /// <param name="isChange">Whether this party receives change.</param>
    /// <returns>This builder for chaining.</returns>
    public TransactionTemplateBuilder<T> AddStaticParty(string partyIdent, string party, bool isChange = false)
    {
        ArgumentNullException.ThrowIfNull(partyIdent);
        ArgumentNullException.ThrowIfNull(party);

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
            TxBuilder = TransactionBuilder.Create(await _provider!.GetParametersAsync().ConfigureAwait(false))
        };

        NetworkType networkType = _provider.NetworkType;

        Dictionary<string, string> parties = ResolveParties(param);

        if (_validFrom > 0)
        {
            _ = context.TxBuilder.SetValidityIntervalStart(_validFrom);
        }

        if (_validTo > 0)
        {
            _ = context.TxBuilder.SetTtl(_validTo);
        }

        WalletAddress changeAddress = WalletAddress.FromBech32(parties["change"]);

        context.AssociationsByInputId["fee"] = [];

        ProcessInputs(param, context);
        ProcessMints(param, context);

        ulong feeBuffer = 5000000;
        List<Value> requiredAmount = [];
        int changeIndex = 0;
        ProcessOutputs(param, context, parties, requiredAmount, ref changeIndex, fee);

        (List<ResolvedInput> utxos, List<ResolvedInput> allUtxos) = await GetAllUtxos(parties, context).ConfigureAwait(false);

        List<Script> scripts = GetScripts(context.IsSmartContractTx, context.ReferenceInputs, allUtxos);

        List<ResolvedInput> specifiedInputsUtxos = GetSpecifiedInputsUtxos(context.SpecifiedInputs, allUtxos);
        context.ResolvedInputs.AddRange(specifiedInputsUtxos);

        CoinSelectionResult coinSelectionResult = PerformCoinSelection(
            utxos,
            requiredAmount,
            specifiedInputsUtxos,
            feeBuffer,
            context
        );

        // Create a prioritized list of inputs for collateral selection
        List<ResolvedInput> prioritizedInputsForCollateral = [.. coinSelectionResult.Inputs];

        if (context.IsSmartContractTx)
        {
            // Add remaining UTXOs that weren't selected
            HashSet<(ReadOnlyMemory<byte> TransactionId, ulong Index)> selectedInputIds = coinSelectionResult.Inputs
                .Select(i => (i.Outref.TransactionId, i.Outref.Index))
                .ToHashSet(new TransactionInputEqualityComparer());

            IOrderedEnumerable<ResolvedInput> remainingUtxos = utxos
                .Where(u => !selectedInputIds.Contains((u.Outref.TransactionId, u.Outref.Index)))
                .OrderByDescending(u => u.Output.Amount().Lovelace());

            prioritizedInputsForCollateral.AddRange(remainingUtxos);
        }

        ulong totalLovelaceChange = coinSelectionResult.LovelaceChange + feeBuffer;
        Lovelace lovelaceChange = new(totalLovelaceChange);

        Dictionary<ReadOnlyMemory<byte>, TokenBundleOutput> assetsChange = coinSelectionResult.AssetsChange;

        foreach (ResolvedInput consumedInput in coinSelectionResult.Inputs)
        {
            context.ResolvedInputs.Add(consumedInput);
            _ = context.TxBuilder.AddInput(consumedInput.Outref);
        }

        Value changeValue = assetsChange.Count > 0
            ? new LovelaceWithMultiAsset(lovelaceChange, new MultiAssetOutput(assetsChange))
            : lovelaceChange;

        TransactionOutput changeOutput = new AlonzoTransactionOutput(new Address(changeAddress.ToBytes()), changeValue, null);

        if (!string.IsNullOrEmpty(_changeAddress) && lovelaceChange.Value > 0)
        {
            _ = context.TxBuilder.AddOutput(changeOutput, true);
        }

        List<TransactionInput> sortedInputs = [.. context.TxBuilder.Body.Inputs.GetValue()
            .OrderBy(e => HexStringCache.ToHexString(e.TransactionId))
            .ThenBy(e => e.Index)];
        _ = context.TxBuilder.SetInputs(sortedInputs);

        List<TransactionInput> sortedRefInputs = [];
        if (context.TxBuilder.Body.ReferenceInputs?.GetValue() != null)
        {
            sortedRefInputs = [.. context.TxBuilder.Body.ReferenceInputs.GetValue()
                .OrderBy(e => HexStringCache.ToHexString(e.TransactionId))
                .ThenBy(e => e.Index)];

            _ = context.TxBuilder.SetReferenceInputs(sortedRefInputs);
        }

        Dictionary<string, int> inputIdToOrderedIndex = GetInputIdToOrderedIndex(context.InputsById, sortedInputs);
        Dictionary<int, Dictionary<string, int>> intIndexedAssociations = [];
        Dictionary<string, (ulong inputIndex, Dictionary<string, int> outputIndices)> stringIndexedAssociations = GetIndexedAssociations(context.AssociationsByInputId, inputIdToOrderedIndex);
        Dictionary<string, ulong> refInputIdToOrderedIndex = GetRefInputIdToOrderedIndex(context.ReferenceInputsById, sortedRefInputs);

        foreach ((string inputId, (ulong inputIndex, Dictionary<string, int> outputIndices) data) in stringIndexedAssociations)
        {
            if (int.TryParse(inputId, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out int intKey))
            {
                intIndexedAssociations[intKey] = data.outputIndices;
            }
        }

        ProcessWithdrawals(param, context, parties);

        if (_requiredSigners.Count > 0)
        {
            foreach (string signer in _requiredSigners)
            {
                WalletAddress address = WalletAddress.FromBech32(parties[signer]);
                _ = context.TxBuilder.AddRequiredSigner(address.GetPaymentKeyHash()!);
            }
        }

        if (context.IsSmartContractTx)
        {
            Dictionary<string, ulong> inputIdToIndex = [];
            Dictionary<string, Dictionary<string, ulong>> outputMappings = [];

            // Create lookup dictionary instead of nested loops
            Dictionary<(ReadOnlyMemory<byte> TransactionId, ulong Index), int> sortedInputsLookup = CreateInputLookup(sortedInputs);

            foreach ((string inputId, TransactionInput input) in context.InputsById)
            {
                (ReadOnlyMemory<byte> TransactionId, ulong Index) inputKey = (input.TransactionId, input.Index);
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

                _ = context.TxBuilder.SetRedeemers(redeemerMap);
            }
        }

        if (_metadataConfig is not null)
        {
            Metadata metadata = _metadataConfig(param);
            _ = context.TxBuilder.SetMetadata(metadata);

            PostMaryTransaction tx = context.TxBuilder.Build();
            AuxiliaryData auxData = tx.AuxiliaryData!;

            _ = context.TxBuilder.SetAuxiliaryDataHash(HashUtil.Blake2b256(CborSerializer.Serialize(auxData)));
        }

        if (_nativeScriptBuilder is not null)
        {
            NativeScript nativeScript = _nativeScriptBuilder(param);
            _ = context.TxBuilder.AddNativeScript(nativeScript);
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
            _ = context.TxBuilder.Evaluate(allUtxos, networkType);
        }

        return context.TxBuilder.CalculateFee(scripts, fee, 1, prioritizedInputsForCollateral).Build();
    }

    /// <summary>
    /// Builds the transaction template into an executable delegate.
    /// </summary>
    /// <param name="eval">Whether to evaluate Plutus scripts.</param>
    /// <returns>A delegate that builds transactions from parameters.</returns>
    public TransactionTemplate<T> Build(bool eval = true)
    {
        return async param =>
        {
            PostMaryTransaction? draftTx = await EvaluateTemplate(param, eval).ConfigureAwait(false) as PostMaryTransaction;
            return await EvaluateTemplate(param, eval, draftTx?.TransactionBody.Fee() ?? 0).ConfigureAwait(false);
        };
    }

    private Dictionary<string, string> ResolveParties(T param)
    {
        Dictionary<string, (string address, bool isChange)> additionalParties = param is ITransactionParameters parameters
            ? parameters.Parties ?? []
            : [];
        return MergeParties(_staticParties, additionalParties);
    }

    private static Dictionary<(ReadOnlyMemory<byte> TransactionId, ulong Index), int> CreateInputLookup(List<TransactionInput> sortedInputs)
    {
        Dictionary<(ReadOnlyMemory<byte>, ulong), int> inputLookup = new(new TransactionInputEqualityComparer());

        for (int i = 0; i < sortedInputs.Count; i++)
        {
            TransactionInput input = sortedInputs[i];
            inputLookup[(input.TransactionId, input.Index)] = i;
        }

        return inputLookup;
    }

    private sealed class TransactionInputEqualityComparer : IEqualityComparer<(ReadOnlyMemory<byte> TransactionId, ulong Index)>
    {
        public bool Equals((ReadOnlyMemory<byte> TransactionId, ulong Index) x, (ReadOnlyMemory<byte> TransactionId, ulong Index) y)
        {
            return x.Index == y.Index && ReadOnlyMemoryComparer.Instance.Equals(x.TransactionId, y.TransactionId);
        }

        public int GetHashCode((ReadOnlyMemory<byte> TransactionId, ulong Index) obj)
        {
            return HashCode.Combine(ReadOnlyMemoryComparer.Instance.GetHashCode(obj.TransactionId), obj.Index);
        }
    }

    private static CoinSelectionResult PerformCoinSelection(
        List<ResolvedInput> utxos,
        List<Value> requiredAmount,
        List<ResolvedInput> specifiedInputsUtxos,
        ulong feeBuffer,
        BuildContext context
    )
    {
        RequirementsResult requirements = CalculateRequirements(requiredAmount, specifiedInputsUtxos, context.Mints);
        requirements.RequiredAmounts.Add(new Lovelace(feeBuffer));
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
        ulong requestedLovelace = requiredAmount.Aggregate(0UL, (sum, amount) => sum + amount.Lovelace());
        Dictionary<string, ulong> requestedAssets = [];

        // Extract requested assets
        foreach (Value amount in requiredAmount)
        {
            if (amount is LovelaceWithMultiAsset lovelaceWithMultiAsset)
            {
                ExtractAssets(lovelaceWithMultiAsset.MultiAsset, requestedAssets);
            }
        }

        // Process specified inputs
        ulong specifiedInputsLovelace = specifiedInputsUtxos.Aggregate(0UL, (sum, utxo) => sum + utxo.Output.Amount().Lovelace());
        Dictionary<string, ulong> specifiedInputsAssets = [];

        foreach (ResolvedInput utxo in specifiedInputsUtxos)
        {
            if (utxo.Output.Amount() is LovelaceWithMultiAsset lovelaceWithMultiAsset)
            {
                ExtractAssets(lovelaceWithMultiAsset.MultiAsset, specifiedInputsAssets);
            }
        }

        // Process minted assets
        Dictionary<string, ulong> mintedAssets = [];
        foreach ((string policyId, Dictionary<string, long> assets) in mints)
        {
            foreach ((string assetName, long amount) in assets)
            {
                string assetKey = BuildAssetKey(policyId, assetName);
                mintedAssets[assetKey] = (ulong)((long)mintedAssets.GetValueOrDefault(assetKey, 0UL) + amount);
            }
        }

        // Adjust requirements based on specified inputs and mints
        ulong adjustedLovelace = requestedLovelace > specifiedInputsLovelace ? requestedLovelace - specifiedInputsLovelace : 0;
        Dictionary<string, ulong> adjustedAssets = AdjustAssetsForMintsAndInputs(requestedAssets, mintedAssets, specifiedInputsAssets);

        // Build required amounts for coin selection
        List<Value> requiredAmounts = BuildRequiredAmounts(adjustedLovelace, adjustedAssets);

        return new RequirementsResult
        {
            OriginalRequestedLovelace = requestedLovelace,
            OriginalRequestedAssets = new Dictionary<string, ulong>(requestedAssets),
            OriginalSpecifiedInputsAssets = specifiedInputsAssets,
            OriginalMintedAssets = mintedAssets,
            RequiredAmounts = requiredAmounts,
            SpecifiedInputsLovelace = specifiedInputsLovelace
        };
    }

    private static void ExtractAssets(MultiAssetOutput? multiAsset, Dictionary<string, ulong> assetDict)
    {
        if (multiAsset?.Value == null)
        {
            return;
        }

        foreach ((ReadOnlyMemory<byte> policyId, TokenBundleOutput tokenBundle) in multiAsset.Value)
        {
            string policyHex = HexStringCache.ToHexString(policyId);

            foreach ((ReadOnlyMemory<byte> assetName, ulong amount) in tokenBundle.Value)
            {
                string assetHex = HexStringCache.ToHexString(assetName);
                string assetKey = BuildAssetKey(policyHex, assetHex);

                assetDict[assetKey] = assetDict.GetValueOrDefault(assetKey, 0UL) + amount;
            }
        }
    }

    private static string BuildAssetKey(string policyId, string assetName)
    {
        lock (BuilderLock)
        {
            int capacity = policyId.Length + assetName.Length;
            StringBuilder builder = new(capacity);
            _ = builder.Append(policyId);
            _ = builder.Append(assetName);
            return builder.ToString().ToUpperInvariant();
        }
    }

    private static Dictionary<string, ulong> AdjustAssetsForMintsAndInputs(
    Dictionary<string, ulong> requestedAssets,
    Dictionary<string, ulong> mintedAssets,
    Dictionary<string, ulong> specifiedInputsAssets)
    {
        Dictionary<string, ulong> adjusted = new(requestedAssets);

        // Subtract minted assets
        foreach ((string assetKey, ulong mintAmount) in mintedAssets)
        {
            if (mintAmount > 0)
            {
                if (adjusted.TryGetValue(assetKey, out ulong requested))
                {
                    adjusted[assetKey] = requested > mintAmount
                        ? requested - mintAmount
                        : 0UL;
                    if (adjusted[assetKey] <= 0)
                    {
                        _ = adjusted.Remove(assetKey);
                    }
                }
            }
            else
            {
                // Burning - add to requirements
                adjusted[assetKey] = adjusted.GetValueOrDefault(assetKey, 0UL) + mintAmount;
            }
        }

        // Subtract specified input assets
        foreach ((string assetKey, ulong inputAmount) in specifiedInputsAssets)
        {
            if (adjusted.TryGetValue(assetKey, out ulong required))
            {
                adjusted[assetKey] = required > inputAmount
                    ? required - inputAmount
                    : 0UL;
                if (adjusted[assetKey] <= 0)
                {
                    _ = adjusted.Remove(assetKey);
                }
            }
        }

        return adjusted;
    }

    private static List<Value> BuildRequiredAmounts(ulong adjustedLovelace, Dictionary<string, ulong> adjustedAssets)
    {
        List<Value> requiredAmounts = [];
        Lovelace lovelace = new(adjustedLovelace);

        if (adjustedAssets.Count == 0)
        {
            requiredAmounts.Add(lovelace);
        }
        else
        {
            Dictionary<ReadOnlyMemory<byte>, TokenBundleOutput> multiAssetDict = new(ReadOnlyMemoryComparer.Instance);

            Dictionary<string, List<KeyValuePair<string, ulong>>> assetsByPolicy = adjustedAssets
                .Where(a => a.Value > 0)
                .GroupBy(a => a.Key[..56])
                .ToDictionary(g => g.Key, g => g.ToList());

            foreach ((string policyIdHex, List<KeyValuePair<string, ulong>> assets) in assetsByPolicy)
            {
                ReadOnlyMemory<byte> policyId = HexStringCache.FromHexString(policyIdHex);
                Dictionary<ReadOnlyMemory<byte>, ulong> tokenBundle = new(ReadOnlyMemoryComparer.Instance);

                foreach (KeyValuePair<string, ulong> asset in assets)
                {
                    ReadOnlyMemory<byte> assetNameBytes = HexStringCache.FromHexString(asset.Key[56..]);
                    tokenBundle[assetNameBytes] = asset.Value;
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

        // Handle asset change
        foreach ((string assetKey, ulong amount) in requirements.OriginalSpecifiedInputsAssets)
        {
            ulong consumedByOutput = requirements.OriginalRequestedAssets.GetValueOrDefault(assetKey, 0UL);
            ulong mintAmount = requirements.OriginalMintedAssets.GetValueOrDefault(assetKey, 0UL);

            if (consumedByOutput < amount && mintAmount >= 0)
            {
                ulong changeAmount = amount - consumedByOutput;
                if (changeAmount > 0)
                {
                    ReadOnlyMemory<byte> policyId = HexStringCache.FromHexString(assetKey[..56]);
                    ReadOnlyMemory<byte> assetNameBytes = HexStringCache.FromHexString(assetKey[56..]);
                    AddAssetToChange(selection.AssetsChange, policyId, assetNameBytes, changeAmount);
                }
            }
        }

        // Handle mint excess
        foreach ((string assetKey, ulong amount) in requirements.OriginalMintedAssets.Where(a => a.Value > 0))
        {
            ulong requiredAmount = requirements.OriginalRequestedAssets.GetValueOrDefault(assetKey, 0UL);
            ulong excessAmount = amount - requiredAmount;

            if (excessAmount > 0)
            {
                ReadOnlyMemory<byte> policyId = HexStringCache.FromHexString(assetKey[..56]);
                ReadOnlyMemory<byte> assetNameBytes = HexStringCache.FromHexString(assetKey[56..]);
                AddAssetToChange(selection.AssetsChange, policyId, assetNameBytes, excessAmount);
            }
        }
    }

    private sealed record RequirementsResult
    {
        public required ulong OriginalRequestedLovelace { get; init; }
        public required Dictionary<string, ulong> OriginalRequestedAssets { get; init; }
        public required Dictionary<string, ulong> OriginalSpecifiedInputsAssets { get; init; }
        public required Dictionary<string, ulong> OriginalMintedAssets { get; init; }
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
                _ = addressesToFetch.Add(resolvedAddress);
            }
        }

        // Single batched call instead of multiple sequential calls
        List<ResolvedInput> allUtxos = await _provider!.GetUtxosAsync([.. addressesToFetch]).ConfigureAwait(false);

        // Separate change UTxOs for specific processing
        string changeAddr = parties["change"];
        List<ResolvedInput> changeUtxos = [.. allUtxos.Where(utxo =>
        {
            string outputAddress = GetAddressFromOutput(utxo.Output);
            return outputAddress == changeAddr;
        })];

        return (changeUtxos, allUtxos);
    }

    private static string GetAddressFromOutput(TransactionOutput output)
    {
        return output switch
        {
            AlonzoTransactionOutput alonzo => WalletAddress.FromBytes(alonzo.Address.Value.ToArray()).ToBech32(),
            PostAlonzoTransactionOutput postAlonzo => WalletAddress.FromBytes(postAlonzo.Address!.Value.ToArray()).ToBech32(),
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
                        ProcessRedeemer(buildContext, redeemerObj, inputIndex);
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
                    ProcessRedeemer(buildContext, redeemerObj, (ulong)withdrawalIndex);
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
                    ProcessRedeemer(buildContext, redeemer, (ulong)mintIndex);
                }
            }

            mintIndex++;
        }
    }

    private static void ProcessRedeemer(BuildContext buildContext, object redeemerObj, ulong index)
    {
        ArgumentNullException.ThrowIfNull(redeemerObj);

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
            catch (InvalidOperationException)
            {
                // Property extraction failed - skip this redeemer
            }
        }
        catch (InvalidOperationException)
        {
            // Dynamic binding failed - skip this redeemer
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
                _ = context.TxBuilder.AddInput(inputOptions.UtxoRef);

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
                _ = context.TxBuilder.AddReferenceInput(referenceInputOptions.UtxoRef);
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

            foreach ((string assetName, long amount) in mintOptions.Assets)
            {
                value[assetName] = amount;
                _ = context.TxBuilder.AddMint(new MultiAssetMint(new Dictionary<ReadOnlyMemory<byte>, TokenBundleMint>(ReadOnlyMemoryComparer.Instance)
                {
                    { HexStringCache.FromHexString(mintOptions.Policy), new TokenBundleMint(new Dictionary<ReadOnlyMemory<byte>, long>(ReadOnlyMemoryComparer.Instance)
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

            TransactionOutput output = outputOptions.BuildOutput(parties, context.TxBuilder.Pparams?.AdaPerUTxOByte ?? 4310);

            _ = context.TxBuilder.AddOutput(output);
            requiredAmount.Add(output.Amount());

            changeIndex++;
            outputIndex++;
        }
    }

    private void ProcessWithdrawals(T param, BuildContext context, Dictionary<string, string> parties)
    {
        Dictionary<RewardAccount, ulong> rewards = [];
        foreach (WithdrawalConfig<T> config in _withdrawalConfigs)
        {
            WithdrawalOptions<T> withdrawalOptions = new() { From = "", Amount = 0 };
            config(withdrawalOptions, param);
            if (withdrawalOptions.From.Length != 0)
            {
                WalletAddress withdrawalAddress = WalletAddress.FromBech32(parties[withdrawalOptions.From]);
                rewards.Add(new RewardAccount(withdrawalAddress.ToBytes()), withdrawalOptions.Amount!);
            }
        }

        if (rewards.Count > 0)
        {
            _ = context.TxBuilder.SetWithdrawals(new Withdrawals(rewards));
        }
    }

    private static List<Script> GetScripts(bool isSmartContractTx, List<TransactionInput> referenceInputs, List<ResolvedInput> allUtxos)
    {
        if (!isSmartContractTx || referenceInputs == null || referenceInputs.Count == 0)
        {
            return [];
        }

        List<Script> scripts = [];

        // Create lookup dictionary for faster matching
        Dictionary<(ReadOnlyMemory<byte>, ulong), ResolvedInput> utxoLookup = new(new TransactionInputEqualityComparer());

        foreach (ResolvedInput utxo in allUtxos)
        {
            utxoLookup[(utxo.Outref.TransactionId, utxo.Outref.Index)] = utxo;
        }

        foreach (TransactionInput referenceInput in referenceInputs)
        {
            (ReadOnlyMemory<byte> TransactionId, ulong Index) key = (referenceInput.TransactionId, referenceInput.Index);
            if (utxoLookup.TryGetValue(key, out ResolvedInput? utxo))
            {
                if (utxo.Output is PostAlonzoTransactionOutput postAlonzoOutput &&
                    postAlonzoOutput.ScriptRef is not null)
                {
                    Script script = CborSerializer.Deserialize<Script>(postAlonzoOutput.ScriptRef.Value.ToArray());
                    scripts.Add(script);
                }
            }
        }

        return scripts;
    }

    private static List<ResolvedInput> GetSpecifiedInputsUtxos(List<TransactionInput> specifiedInputs, List<ResolvedInput> allUtxos)
    {
        // Create lookup dictionary for O(1) access instead of O(n) per input
        Dictionary<(ReadOnlyMemory<byte>, ulong), ResolvedInput> utxoLookup = new(new TransactionInputEqualityComparer());

        foreach (ResolvedInput utxo in allUtxos)
        {
            utxoLookup[(utxo.Outref.TransactionId, utxo.Outref.Index)] = utxo;
        }

        List<ResolvedInput> specifiedInputsUtxos = new(specifiedInputs.Count);

        foreach (TransactionInput input in specifiedInputs)
        {
            (ReadOnlyMemory<byte> TransactionId, ulong Index) key = (input.TransactionId, input.Index);
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

        // Create lookup dictionary using memory comparer for transaction IDs
        Dictionary<(ReadOnlyMemory<byte> TransactionId, ulong Index), int> inputLookup = CreateInputLookup(sortedInputs);

        foreach ((string inputId, TransactionInput input) in inputsById)
        {
            (ReadOnlyMemory<byte> TransactionId, ulong Index) key = (input.TransactionId, input.Index);
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

        // Create lookup dictionary to avoid nested loops
        Dictionary<(ReadOnlyMemory<byte>, ulong), int> refInputLookup = new(new TransactionInputEqualityComparer());

        for (int i = 0; i < sortedRefInputs.Count; i++)
        {
            TransactionInput refInput = sortedRefInputs[i];
            refInputLookup[(refInput.TransactionId, refInput.Index)] = i;
        }

        foreach ((string refInputId, TransactionInput refInput) in refInputsById)
        {
            (ReadOnlyMemory<byte> TransactionId, ulong Index) key = (refInput.TransactionId, refInput.Index);
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

    private static void AddAssetToChange(
        Dictionary<ReadOnlyMemory<byte>, TokenBundleOutput> assetsChange,
        ReadOnlyMemory<byte> policyId,
        ReadOnlyMemory<byte> assetName,
        ulong amount
    )
    {
        if (assetsChange.TryGetValue(policyId, out TokenBundleOutput? existingPolicy))
        {
            Dictionary<ReadOnlyMemory<byte>, ulong> tokenBundle = existingPolicy.Value;

            if (tokenBundle.TryGetValue(assetName, out ulong existingAmount))
            {
                ulong newAmount = existingAmount + amount;
                if (newAmount <= 0)
                {
                    _ = tokenBundle.Remove(assetName);
                }
                else
                {
                    tokenBundle[assetName] = newAmount;
                }
            }
            else if (amount > 0)
            {
                tokenBundle[assetName] = amount;
            }
        }
        else if (amount > 0)
        {
            Dictionary<ReadOnlyMemory<byte>, ulong> tokenBundle = new(ReadOnlyMemoryComparer.Instance)
            {
                [assetName] = amount
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

    private sealed class BuildContext
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
