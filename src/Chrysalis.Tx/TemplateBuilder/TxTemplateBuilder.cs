using Chrysalis.Cbor.Types.Cardano.Core.Certificates;
using Chrysalis.Cbor.Types.Cardano.Core.Common;
using Chrysalis.Cbor.Types.Cardano.Core.Transaction;
using Chrysalis.Cbor.Types.Cardano.Core.TransactionWitness;
using Chrysalis.Tx.Extensions;
using Chrysalis.Tx.Models;
using Chrysalis.Tx.TransactionBuilding;
using Chrysalis.Tx.TransactionBuilding.Extensions;
using Chrysalis.Tx.Utils;
using TxAddr = Chrysalis.Tx.Models.Addresses;

namespace Chrysalis.Tx.TemplateBuilder;

public class TxTemplateBuilder<T>
{
    private IProvider? _provider;
    private readonly List<Action<InputOptions, T>> _inputConfigs = [];
    private readonly List<Action<OutputOptions, T>> _outputConfigs = [];
    private readonly List<Action<WithdrawalOptions<T>, T>> _withdrawalConfigs = [];
    private readonly Dictionary<string, string> _staticParties = [];
    private Action<Dictionary<int, Dictionary<string, int>>, T, Dictionary<RedeemerKey, RedeemerValue>>? _redeemerGenerator;

    public bool TrackAssociations { get; set; } = true;

    public static TxTemplateBuilder<T> Create(IProvider provider) => 
        new TxTemplateBuilder<T>().SetProvider(provider);

    private TxTemplateBuilder<T> SetProvider(IProvider provider)
    {
        _provider = provider;
        return this;
    }

    public TxTemplateBuilder<T> AddInput(Action<InputOptions, T> config)
    {
        _inputConfigs.Add(config);
        return this;
    }

    public TxTemplateBuilder<T> AddOutput(Action<OutputOptions, T> config)
    {
        _outputConfigs.Add(config);
        return this;
    }

    public TxTemplateBuilder<T> AddWithdrawal(Action<WithdrawalOptions<T>, T> config)
    {
        _withdrawalConfigs.Add(config);
        return this;
    }

    public TxTemplateBuilder<T> SetRedeemerGenerator(Action<Dictionary<int, Dictionary<string, int>>, T, Dictionary<RedeemerKey, RedeemerValue>>? generator)
    {
        _redeemerGenerator = generator;
        return this;
    }

    public TxTemplateBuilder<T> AddStaticParty(string partyIdent, string party)
    {
        _staticParties[partyIdent] = party;
        return this;
    }

    public Func<T, Task<PostMaryTransaction>> Build()
    {
        return async param =>
        {
            if (_provider == null)
                throw new InvalidOperationException("Provider has not been set");

            var parameters = await _provider.GetParametersAsync();
            var txBuilder = TransactionBuilder.Create(parameters);
            
            var parties = GetMergedParties(param);
            var txContext = await ConfigureInputs(txBuilder, param, parties);
            
            ConfigureOutputs(txBuilder, param, parties, txContext);
            
            var utxos = await _provider.GetUtxosAsync(txContext.SenderAddress!.ToBech32());
            var allUtxos = new List<ResolvedInput>(utxos);
            
            if (txContext.SenderAddress.ToBech32() != txContext.ChangeAddress.ToBech32())
            {
                var changeAddressUtxos = await _provider.GetUtxosAsync(txContext.ChangeAddress.ToBech32());
                allUtxos.AddRange(changeAddressUtxos);
                utxos = changeAddressUtxos;
            }

            TxTemplateBuilder<T>.AddFeeInput(txBuilder, utxos);
            TxTemplateBuilder<T>.AddCollateralIfNeeded(txBuilder, utxos, txContext.IsSmartContractTx);
            
            var coinSelectionResult = TxTemplateBuilder<T>.PerformCoinSelection(utxos, txContext);

            TxTemplateBuilder<T>.AddSelectedCoins(txBuilder, coinSelectionResult);
            
            var changeOutput = TxTemplateBuilder<T>.CreateChangeOutput(coinSelectionResult, txContext);
            txBuilder.AddOutput(changeOutput, true);

            TxTemplateBuilder<T>.SortInputsAndUpdateAssociations(txBuilder, txContext);
            
            ConfigureWithdrawals(txBuilder, param, txContext);
            
            if (txContext.IsSmartContractTx)
            {
                txBuilder.SetRedeemers(new RedeemerMap(txContext.Redeemers));
                // Commented line in the original: txBuilder.Evaluate(allUtxos);
            }

            return txBuilder
                .CalculateFee(txContext.ScriptCborBytes)
                .Build();
        };
    }

    private Dictionary<string, string> GetMergedParties(T param)
    {
        var additionalParties = param is IParameters parameters
            ? parameters.Parties ?? []
            : [];

        return MergeParties(_staticParties, additionalParties);
    }

    private async Task<TransactionContext> ConfigureInputs(TransactionBuilder txBuilder, T param, Dictionary<string, string> parties)
    {
        var context = new TransactionContext
        {
            InputsById = [],
            AssociationsByInputId = [],
            Redeemers = []
        };

        foreach (var config in _inputConfigs)
        {
            var inputOptions = new InputOptions("", null, null, null, null);
            config(inputOptions, param);

            if (TrackAssociations && !string.IsNullOrEmpty(inputOptions.Id) && inputOptions.UtxoRef != null)
            {
                context.InputsById[inputOptions.Id] = inputOptions.UtxoRef;
                context.AssociationsByInputId[inputOptions.Id] = [];
            }

            ProcessInputOptions(txBuilder, inputOptions, parties, context);
        }

        if (context.SenderAddress == null)
            throw new InvalidOperationException("No sender address was configured");

        context.ChangeAddress = context.SenderAddress;

        if (context.IsSmartContractTx && context.ReferenceInput != null)
        {
            context.ScriptCborBytes = await GetScriptCborBytes(context.ReferenceInput, context.SenderAddress);
        }

        return context;
    }

    private void ProcessInputOptions(
        TransactionBuilder txBuilder, 
        InputOptions inputOptions, 
        Dictionary<string, string> parties, 
        TransactionContext context)
    {
        if (inputOptions.UtxoRef is not null)
        {
            if (inputOptions.IsReference)
            {
                txBuilder.AddReferenceInput(inputOptions.UtxoRef);
                context.ReferenceInput = inputOptions.UtxoRef;
                context.IsSmartContractTx = true;
            }
            else
            {
                context.SpecifiedInputs.Add(inputOptions.UtxoRef);
                txBuilder.AddInput(inputOptions.UtxoRef);
                TxTemplateBuilder<T>.ProcessRedeemer(inputOptions.Redeemer, context.Redeemers);
            }
        }

        if (inputOptions.MinAmount is not null)
        {
            context.MinimumLovelace += inputOptions.MinAmount switch
            {
                Lovelace lovelace => lovelace.Value,
                LovelaceWithMultiAsset multiAsset => multiAsset.LovelaceValue.Value,
                _ => throw new ArgumentException($"Invalid value type: {inputOptions.MinAmount.GetType().Name}")
            };
        }

        if (!string.IsNullOrEmpty(inputOptions.From))
        {
            context.SenderAddress = TxAddr.Address.FromBech32(parties[inputOptions.From]);
        }
    }

    private static void ProcessRedeemer(object? redeemer, Dictionary<RedeemerKey, RedeemerValue> redeemers)
    {
        if (redeemer == null) return;

        switch (redeemer)
        {
            case RedeemerMap redeemersMap:
                foreach (var kvp in redeemersMap.Value)
                {
                    redeemers[kvp.Key] = kvp.Value;
                }
                break;
            case RedeemerList redeemersList:
                foreach (var redeemerItem in redeemersList.Value)
                {
                    redeemers[new RedeemerKey(redeemerItem.Tag, redeemerItem.Index)] = 
                        new RedeemerValue(redeemerItem.Data, redeemerItem.ExUnits);
                }
                break;
            default:
                throw new ArgumentException($"Unsupported redeemer type: {redeemer.GetType().Name}");
        }
    }

    private async Task<byte[]> GetScriptCborBytes(TransactionInput referenceInput, TxAddr.Address senderAddress)
    {
        var utxos = await _provider!.GetUtxosAsync(senderAddress.ToBech32());
        
        foreach (var utxo in utxos)
        {
            if (Convert.ToHexString(utxo.Outref.TransactionId) == Convert.ToHexString(referenceInput.TransactionId) && 
                utxo.Outref.Index == referenceInput.Index)
            {
                return utxo.Output switch
                {
                    PostAlonzoTransactionOutput postAlonzoOutput => 
                        postAlonzoOutput.ScriptRef?.Value ?? Array.Empty<byte>(),
                    _ => throw new InvalidOperationException($"Invalid output type: {utxo.Output.GetType().Name}")
                };
            }
        }
        
        return [];
    }

    private void ConfigureOutputs(
        TransactionBuilder txBuilder, 
        T param, 
        Dictionary<string, string> parties, 
        TransactionContext context)
    {
        int outputIndex = 0;
        
        foreach (var config in _outputConfigs)
        {
            var outputOptions = new OutputOptions("", null, null);
            config(outputOptions, param);
            
            if (TrackAssociations &&
                !string.IsNullOrEmpty(outputOptions.AssociatedInputId) &&
                !string.IsNullOrEmpty(outputOptions.Id) &&
                context.AssociationsByInputId.TryGetValue(outputOptions.AssociatedInputId, out var associations))
            {
                associations[outputOptions.Id] = outputIndex;
            }
            
            context.RequiredAmount.Add(outputOptions.Amount!);
            txBuilder.AddOutput(OutputUtils.BuildOutput(outputOptions, parties));
            
            if (context.IsSmartContractTx && !string.IsNullOrEmpty(outputOptions.To))
            {
                context.ChangeAddress = TxAddr.Address.FromBech32(parties[outputOptions.To]);
            }
            
            outputIndex++;
        }
    }

    private static void AddFeeInput(TransactionBuilder txBuilder, List<ResolvedInput> utxos)
    {
        ResolvedInput? feeInput = utxos
            .Where(e => e.Output.Amount().Lovelace() >= 5_000_000UL && e.Output.Amount() is Lovelace)
            .OrderByDescending(e => e.Output.Amount().Lovelace())
            .FirstOrDefault();
            
        if (feeInput is not null)
        {
            utxos.Remove(feeInput);
            txBuilder.AddInput(feeInput.Outref);
        }
    }

    private static void AddCollateralIfNeeded(TransactionBuilder txBuilder, List<ResolvedInput> utxos, bool isSmartContractTx)
    {
        if (!isSmartContractTx) return;
        
        ResolvedInput? collateralInput = utxos
            .Where(e => e.Output.Amount().Lovelace() >= 5_000_000UL && e.Output.Amount() is Lovelace)
            .OrderByDescending(e => e.Output.Amount().Lovelace())
            .FirstOrDefault();
            
        if (collateralInput is not null)
        {
            utxos.Remove(collateralInput);
            txBuilder.SetCollateral(collateralInput);
        }
    }

    private static CoinSelectionResult PerformCoinSelection(List<ResolvedInput> utxos, TransactionContext context)
    {
        List<ResolvedInput> specifiedInputsUtxos = [];
        List<ResolvedInput> allUtxos = utxos.ToList();
        
        foreach (var input in context.SpecifiedInputs)
        {
            ResolvedInput specifiedInputUtxo = allUtxos.First(e => 
                Convert.ToHexString(e.Outref.TransactionId) == Convert.ToHexString(input.TransactionId) && 
                e.Outref.Index == input.Index);
                
            specifiedInputsUtxos.Add(specifiedInputUtxo);
            utxos.Remove(specifiedInputUtxo);
        }
        
        return CoinSelectionAlgorithm.LargestFirstAlgorithm(
            utxos, 
            context.RequiredAmount, 
            specifiedInputs: specifiedInputsUtxos, 
            minimumAmount: new Lovelace(context.MinimumLovelace));
    }

    private static void AddSelectedCoins(TransactionBuilder txBuilder, CoinSelectionResult coinSelectionResult)
    {
        foreach (var consumedInput in coinSelectionResult.Inputs)
        {
            txBuilder.AddInput(consumedInput.Outref);
        }
    }

    private static AlonzoTransactionOutput CreateChangeOutput(CoinSelectionResult coinSelectionResult, TransactionContext context)
    {
        ulong totalLovelaceChange = coinSelectionResult.LovelaceChange;
        Dictionary<byte[], TokenBundleOutput> assetsChange = coinSelectionResult.AssetsChange;
        
        var lovelaceChange = new Lovelace(totalLovelaceChange);
        Value changeValue = lovelaceChange;
        
        if (assetsChange.Count > 0)
        {
            changeValue = new LovelaceWithMultiAsset(lovelaceChange, new MultiAssetOutput(assetsChange));
        }
        
        return new AlonzoTransactionOutput(
            new Address(context.ChangeAddress.ToBytes()),
            changeValue,
            null
        );
    }

    private static void SortInputsAndUpdateAssociations(TransactionBuilder txBuilder, TransactionContext context)
    {
        txBuilder.bodyBuilder.Inputs = [.. txBuilder.bodyBuilder.Inputs
            .OrderBy(e => Convert.ToHexString(e.TransactionId))
            .ThenBy(e => e.Index)];
            
        List<TransactionInput> sortedInputs = txBuilder.bodyBuilder.Inputs;
        Dictionary<string, int> inputIdToOrderedIndex = [];
        
        foreach (var (inputId, input) in context.InputsById)
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
        
        Dictionary<int, Dictionary<string, int>> indexedAssociations = [];
        
        foreach (var (inputId, roleToOutputIndex) in context.AssociationsByInputId)
        {
            if (!inputIdToOrderedIndex.TryGetValue(inputId, out int inputIndex))
                continue;
                
            indexedAssociations[inputIndex] = new Dictionary<string, int>(roleToOutputIndex);
        }
        
        context.IndexedAssociations = indexedAssociations;
    }


    private void ConfigureWithdrawals(TransactionBuilder txBuilder, T param, TransactionContext context)
    {
        Dictionary<RewardAccount, ulong> rewards = [];
        
        foreach (var config in _withdrawalConfigs)
        {
            var withdrawalOptions = new WithdrawalOptions<T>("", 0);
            config(withdrawalOptions, param);
            
            TxAddr.Address withdrawalAddress = TxAddr.Address.FromBech32(context.Parties[withdrawalOptions.From]);
            rewards.Add(new RewardAccount(withdrawalAddress.ToBytes()), withdrawalOptions.Amount!);
            
            if (withdrawalOptions.Redeemers is not null)
            {
                foreach (var kvp in withdrawalOptions.Redeemers.Value)
                {
                    context.Redeemers[kvp.Key] = kvp.Value;
                }
                continue;
            }
            
            if (withdrawalOptions.RedeemerGenerator is not null)
            {
                withdrawalOptions.RedeemerGenerator(context.IndexedAssociations, param, context.Redeemers);
            }
        }
        
        if (rewards.Count > 0)
        {
            txBuilder.SetWithdrawals(new Withdrawals(rewards));
        }
    }

    private Dictionary<string, string> MergeParties(
        Dictionary<string, string> staticParties,
        Dictionary<string, string> dynamicParties)
    {
        var result = new Dictionary<string, string>(staticParties);
        foreach (var (key, value) in dynamicParties)
        {
            result[key] = value;
        }
        return result;
    }

 
    private class TransactionContext
    {
        public Dictionary<string, string> Parties { get; set; } = [];
        public Dictionary<string, TransactionInput> InputsById { get; set; } = [];
        public Dictionary<string, Dictionary<string, int>> AssociationsByInputId { get; set; } = [];
        public Dictionary<int, Dictionary<string, int>> IndexedAssociations { get; set; } = [];
        public Dictionary<RedeemerKey, RedeemerValue> Redeemers { get; set; } = [];
        public List<TransactionInput> SpecifiedInputs { get; set; } = [];
        public List<Value> RequiredAmount { get; set; } = [];
        public TxAddr.Address? SenderAddress { get; set; }
        public TxAddr.Address ChangeAddress { get; set; } = null!;
        public TransactionInput? ReferenceInput { get; set; }
        public bool IsSmartContractTx { get; set; }
        public ulong MinimumLovelace { get; set; }
        public byte[] ScriptCborBytes { get; set; } = [];
    }
}