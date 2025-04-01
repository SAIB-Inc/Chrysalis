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

            var coinSelectionResult = CoinSelectionUtil.LargestFirstAlgorithm(
                utxos,
                requiredAmount,
                specifiedInputs: specifiedInputsUtxos
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
                        outputMappings[inputId] = new Dictionary<string, ulong>();
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

            if (string.IsNullOrEmpty(inputOptions.Id) || !inputIdToIndex.TryGetValue(inputOptions.Id, out var inputIndex))
                continue;

            if(inputOptions.Redeemer != null){
                foreach(RedeemerKey key in inputOptions.Redeemer.Value.Keys)
                {
                   buildContext.Redeemers[key] = inputOptions.Redeemer.Value[key];
                }
            }

            if (inputOptions.RedeemerBuilder != null)
            {
                var redeemerObj = inputOptions.RedeemerBuilder(mapping, param);

                if (redeemerObj != null)
                {
                    ProcessRedeemer(buildContext, redeemerObj, inputIndex);
                }
            }
        }

        int withdrawalIndex = 0;
        foreach (var config in _withdrawalConfigs)
        {
            var withdrawalOptions = new WithdrawalOptions<T> { From = "", Amount = 0 };
            config(withdrawalOptions, param);
            

            if(withdrawalOptions.Redeemer != null){
                foreach(RedeemerKey key in withdrawalOptions.Redeemer.Value.Keys)
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

        int policyIndex = 0;
        foreach (var config in _mintConfigs)
        {
            var mintOptions = new MintOptions<T> { Policy = "", Assets = [] };
            config(mintOptions, param);

            if(mintOptions.Redeemer != null){
                foreach(RedeemerKey key in mintOptions.Redeemer.Value.Keys)
                {
                   buildContext.Redeemers[key] = mintOptions.Redeemer.Value[key];
                }
            }

            if (mintOptions.RedeemerBuilder != null)
            {
                Redeemer<CborBase> redeemer = mintOptions.RedeemerBuilder(mapping, param);

                if (redeemer != null)
                {
                    ProcessRedeemer(buildContext, redeemer, (ulong)policyIndex);
                }
            }

            policyIndex++;
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
            var mintOptions = new MintOptions<T> { Policy = "", Assets = new Dictionary<string, ulong>() };
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

            ChrysalisWallet.Address withdrawalAddress = ChrysalisWallet.Address.FromBech32(parties[withdrawalOptions.From]);
            rewards.Add(new RewardAccount(withdrawalAddress.ToBytes()), withdrawalOptions.Amount!);

            // Redeemers for withdrawals will be handled in the BuildRedeemers method

            // The new redeemer approach doesn't use the RedeemerBuilder
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