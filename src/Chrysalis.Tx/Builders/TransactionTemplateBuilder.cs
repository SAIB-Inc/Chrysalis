using Chrysalis.Cbor.Types.Cardano.Core.Certificates;
using Chrysalis.Cbor.Types.Cardano.Core.Common;
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
    private readonly List<Action<MintOptions, T>> _mintConfigs = [];
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

    public TransactionTemplateBuilder<T> AddMint(Action<MintOptions, T> config)
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

    public Func<T, Task<PostMaryTransaction>> Build()
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
                specifiedInputs: specifiedInputsUtxos,
                minimumAmount: new Lovelace(context.MinimumLovelace)
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

            List<TransactionInput> sortedInputs = [.. context.TxBuilder.body.Inputs.Value().OrderBy(e => Convert.ToHexString(e.TransactionId)).ThenBy(e => e.Index)];
            context.TxBuilder.SetInputs(sortedInputs);

            var inputIdToOrderedIndex = GetInputIdToOrderedIndex(context.InputsById, sortedInputs);
            var indexedAssociations = GetIndexedAssociations(context.AssociationsByInputId, inputIdToOrderedIndex);

            ProcessWithdrawals(param, context, parties, indexedAssociations);

            if (context.IsSmartContractTx)
            {
                context.TxBuilder.SetRedeemers(new RedeemerMap(context.Redeemers));
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

    private void ProcessInputs(T param, BuildContext context)
    {
        foreach (var config in _inputConfigs)
        {
            var inputOptions = new InputOptions<T>("", null, null, null, null, null, null);
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
                    if (inputOptions.Redeemer is not null)
                    {
                        foreach (var kvp in inputOptions.Redeemer.Value)
                        {
                            context.Redeemers[kvp.Key] = kvp.Value;
                        }
                    }
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
            var mintOptions = new MintOptions("", []);
            config(mintOptions, param);

            foreach (var (assetName, amount) in mintOptions.Assets)
            {
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
            var outputOptions = new OutputOptions("", null, null);
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
            var withdrawalOptions = new WithdrawalOptions<T>("", 0, null, null);
            config(withdrawalOptions, param);

            ChrysalisWallet.Address withdrawalAddress = ChrysalisWallet.Address.FromBech32(parties[withdrawalOptions.From]);
            rewards.Add(new RewardAccount(withdrawalAddress.ToBytes()), withdrawalOptions.Amount!);

            if (withdrawalOptions.Redeemers is not null)
            {
                foreach (var kvp in withdrawalOptions.Redeemers.Value)
                {
                    context.Redeemers[kvp.Key] = kvp.Value;
                }
            }
            else if (withdrawalOptions.RedeemerBuilder is not null)
            {
                withdrawalOptions.RedeemerBuilder(indexedAssociations, param, context.Redeemers);
            }
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

    private Dictionary<int, Dictionary<string, int>> GetIndexedAssociations(Dictionary<string, Dictionary<string, int>> associationsByInputId, Dictionary<string, int> inputIdToOrderedIndex)
    {
        Dictionary<int, Dictionary<string, int>> indexedAssociations = [];
        foreach (var (inputId, roleToOutputIndex) in associationsByInputId)
        {
            if (inputIdToOrderedIndex.TryGetValue(inputId, out int inputIndex))
            {
                indexedAssociations[inputIndex] = new Dictionary<string, int>(roleToOutputIndex);
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
    }
}