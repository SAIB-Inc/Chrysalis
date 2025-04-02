using Chrysalis.Cbor.Extensions;
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
using ChrysalisWallet = Chrysalis.Wallet.Models.Addresses;
using WalletAddress = Chrysalis.Wallet.Models.Addresses.Address;

namespace Chrysalis.Tx.Builders;

public class TransactionTemplateBuilder<T>
{
    private ICardanoDataProvider? _provider;
    private string? _changeAddress;
    private readonly Dictionary<string, string> _staticParties = [];
    private readonly List<Action<InputOptions<T>, T>> _inputConfigs = [];
    private readonly List<Action<OutputOptions, T>> _outputConfigs = [];
    private readonly List<Action<MintOptions<T>, T>> _mintConfigs = [];
    private readonly List<Action<WithdrawalOptions<T>, T>> _withdrawalConfigs = [];
    private readonly List<string> requiredSigners = [];

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

            ChrysalisWallet.Address changeAddress = ChrysalisWallet.Address.FromBech32(parties[_changeAddress]);

            ProcessInputs(param, context);
            ProcessMints(param, context);

            List<Value> requiredAmount = [];
            int changeIndex = 0;
            ProcessOutputs(param, context, parties, requiredAmount, ref changeIndex);

            List<ResolvedInput> utxos = await _provider!.GetUtxosAsync(parties[_changeAddress]);
            var allUtxos = new List<ResolvedInput>(utxos);
            foreach (string address in context.InputAddresses.Distinct())
            {
                if (address != _changeAddress)
                {
                    allUtxos.AddRange(await _provider!.GetUtxosAsync(parties[address]));
                }
            }

            byte[] scriptCborBytes = GetScriptCborBytes(context.IsSmartContractTx, context.ReferenceInput, allUtxos);

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
            Value changeValue = assetsChange.Count > 0
                ? new LovelaceWithMultiAsset(lovelaceChange, new MultiAssetOutput(assetsChange))
                : lovelaceChange;

            var changeOutput = new AlonzoTransactionOutput(new Address(changeAddress.ToBytes()), changeValue, null);
            context.TxBuilder.AddOutput(changeOutput, true);

            List<TransactionInput> sortedInputs = [.. context.TxBuilder.body.Inputs.GetValue().OrderBy(e => Convert.ToHexString(e.TransactionId)).ThenBy(e => e.Index)];
            context.TxBuilder.SetInputs(sortedInputs);

            var inputIdToOrderedIndex = GetInputIdToOrderedIndex(context.InputsById, sortedInputs);
            var intIndexedAssociations = new Dictionary<int, Dictionary<string, int>>();
            var stringIndexedAssociations = GetIndexedAssociations(context.AssociationsByInputId, inputIdToOrderedIndex);

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

                BuildRedeemers(context, param, inputIdToIndex, outputMappings);

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
                    context.TxBuilder.AddRequiredSigner(address.ToBytes());
                }
            }

            return context.TxBuilder.CalculateFee(scriptCborBytes).Build();
        };
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

    private void BuildRedeemers(BuildContext buildContext, T param, Dictionary<string, ulong> inputIdToIndex, Dictionary<string, Dictionary<string, ulong>> outputMappings)
    {
        var mapping = new InputOutputMapping();

        foreach (var (inputId, inputIndex) in inputIdToIndex)
        {
            mapping.AddInput(inputId, inputIndex);
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
            var inputOptions = new InputOptions<T> { From = "", Id = null, IsReference = false };
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
            var inputOptions = new InputOptions<T> { From = "", Id = null, IsReference = false };
            config(inputOptions, param);

            if (!string.IsNullOrEmpty(inputOptions.Id) && inputOptions.UtxoRef != null)
            {
                context.InputsById[inputOptions.Id] = inputOptions.UtxoRef;
                context.AssociationsByInputId[inputOptions.Id] = [];
            }

            if (inputOptions.UtxoRef is not null)
            {
                if (inputOptions.IsReference)
                {
                    context.TxBuilder.AddReferenceInput(inputOptions.UtxoRef);
                    context.ReferenceInput = inputOptions.UtxoRef;
                    context.IsSmartContractTx = true;
                }
                else
                {
                    context.SpecifiedInputs.Add(inputOptions.UtxoRef);
                    context.TxBuilder.AddInput(inputOptions.UtxoRef);
                }
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

            requiredAmount.Add(outputOptions.Amount!);
            context.TxBuilder.AddOutput(outputOptions.BuildOutput(parties));
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

    private byte[] GetScriptCborBytes(bool isSmartContractTx, TransactionInput? referenceInput, List<ResolvedInput> allUtxos)
    {
        if (isSmartContractTx && referenceInput != null)
        {
            foreach (var utxo in allUtxos)
            {
                if (Convert.ToHexString(utxo.Outref.TransactionId) == Convert.ToHexString(referenceInput.TransactionId) &&
                    utxo.Outref.Index == referenceInput.Index)
                {
                    return utxo.Output switch
                    {
                        PostAlonzoTransactionOutput postAlonzoOutput => postAlonzoOutput.ScriptRef?.Value ?? [],
                        _ => throw new InvalidOperationException($"Invalid output type: {utxo.Output.GetType().Name}")
                    };
                }
            }
        }
        return [];
    }

    private ResolvedInput? SelectFeeInput(List<ResolvedInput> utxos)
    {
        return utxos
            .Where(e => e.Output.Amount().Lovelace() >= 5_000_000UL && e.Output.Amount() is Lovelace)
            .OrderByDescending(e => e.Output.Amount().Lovelace())
            .FirstOrDefault();
    }

    private ResolvedInput? SelectCollateralInput(List<ResolvedInput> utxos, bool isSmartContractTx)
    {
        if (!isSmartContractTx) return null;
        return utxos
            .Where(e => e.Output.Amount().Lovelace() >= 5_000_000UL && e.Output.Amount() is Lovelace)
            .OrderByDescending(e => e.Output.Amount().Lovelace())
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
        public TransactionInput? ReferenceInput { get; set; }
        public ulong MinimumLovelace { get; set; }
        public Dictionary<string, TransactionInput> InputsById { get; } = [];
        public Dictionary<string, Dictionary<string, int>> AssociationsByInputId { get; } = [];
        public List<string> InputAddresses { get; } = [];
        public List<TransactionInput> SpecifiedInputs { get; } = [];
        public Dictionary<RedeemerKey, RedeemerValue> Redeemers { get; } = [];
        public Dictionary<string, Dictionary<string, long>> Mints { get; } = [];
    }
}