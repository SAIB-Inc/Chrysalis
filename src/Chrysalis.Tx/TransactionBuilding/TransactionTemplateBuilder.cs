using Chrysalis.Cbor.Types.Cardano.Core.Certificates;
using Chrysalis.Cbor.Types.Cardano.Core.Common;
using Chrysalis.Cbor.Types.Cardano.Core.Transaction;
using Chrysalis.Cbor.Types.Cardano.Core.TransactionWitness;
using Chrysalis.Tx.Extensions;
using Chrysalis.Tx.Models;
using Chrysalis.Tx.TransactionBuilding.Extensions;
using Chrysalis.Tx.Utils;
using ChrysalisWallet = Chrysalis.Wallet.Addresses;

namespace Chrysalis.Tx.TransactionBuilding;
public class TransactionTemplateBuilder<T>
{
    private IProvider? _provider;
    private readonly List<Action<InputOptions, T>> _inputConfigs = [];
    private readonly List<Action<OutputOptions, T>> _outputConfigs = [];
    private readonly Dictionary<string, string> _staticParties = [];
    private readonly List<Action<WithdrawalOptions<T>, T>> _withdrawalConfigs = [];

    private Action<Dictionary<int, Dictionary<string, int>>, T, Dictionary<RedeemerKey, RedeemerValue>>? _redeemerGenerator;

    public static TransactionTemplateBuilder<T> Create(IProvider provider) => new TransactionTemplateBuilder<T>().SetProvider(provider);

    private TransactionTemplateBuilder<T> SetProvider(IProvider provider)
    {
        _provider = provider;
        return this;
    }

    public TransactionTemplateBuilder<T> AddInput(Action<InputOptions, T> config)
    {
        _inputConfigs.Add(config);
        return this;
    }

    public TransactionTemplateBuilder<T> AddOutput(Action<OutputOptions, T> config)
    {
        _outputConfigs.Add(config);
        return this;
    }

    public TransactionTemplateBuilder<T> AddWithdrawal(Action<WithdrawalOptions<T>, T> config)
    {
        _withdrawalConfigs.Add(config);
        return this;
    }


    public TransactionTemplateBuilder<T> SetRedeemerGenerator(Action<Dictionary<int, Dictionary<string, int>>, T, Dictionary<RedeemerKey, RedeemerValue>>? generator)
    {
        _redeemerGenerator = generator;
        return this;
    }

    public TransactionTemplateBuilder<T> AddStaticParty(string partyIdent, string party)
    {
        _staticParties[partyIdent] = party;
        return this;
    }


    public Func<T, Task<PostMaryTransaction>> Build()
    {
        return async param =>
        {
            var pparams = await _provider!.GetParametersAsync();
            var txBuilder = TransactionBuilder.Create(pparams);

            int changeIndex = 0;

            var additionalParties = param is IParameters parameters
                ? parameters.Parties ?? []
                : [];

            var parties = MergeParties(_staticParties, additionalParties);

            Dictionary<string, TransactionInput> inputsById = [];
            Dictionary<string, Dictionary<string, int>> associationsByInputId = [];

            ChrysalisWallet.Address? senderAddress = null;
            List<TransactionInput> specifiedInputs = [];
            TransactionInput? referenceInput = null;
            bool isSmartContractTx = false;
            ulong minimumLovelace = 0;
            Dictionary<RedeemerKey, RedeemerValue> redeemers = [];

            foreach (var config in _inputConfigs)
            {
                var inputOptions = new InputOptions("", null, null, null, null);
                config(inputOptions, param);

                if (!string.IsNullOrEmpty(inputOptions.Id) && inputOptions.UtxoRef != null)
                {
                    inputsById[inputOptions.Id] = inputOptions.UtxoRef;
                    associationsByInputId[inputOptions.Id] = [];
                }

                if (inputOptions.UtxoRef is not null)
                {
                    if (inputOptions.IsReference)
                    {
                        txBuilder.AddReferenceInput(inputOptions.UtxoRef);
                        referenceInput = inputOptions.UtxoRef;
                        isSmartContractTx = true;
                        continue;
                    }
                    else
                    {
                        specifiedInputs.Add(inputOptions.UtxoRef);
                        senderAddress = ChrysalisWallet.Address.FromBech32(parties[inputOptions.From]);
                        txBuilder.AddInput(inputOptions.UtxoRef);

                        if (inputOptions.Redeemer is not null)
                        {
                            foreach (var kvp in inputOptions.Redeemer.Value)
                            {
                                redeemers[kvp.Key] = kvp.Value;
                            }
                            break;

                        }
                    }
                }

                if (inputOptions.MinAmount is not null)
                {
                    minimumLovelace += inputOptions.MinAmount.Lovelace();
                }

                if (!string.IsNullOrEmpty(inputOptions.From))
                {
                    senderAddress = ChrysalisWallet.Address.FromBech32(parties[inputOptions.From]);
                }
            }

            ChrysalisWallet.Address changeAddress = senderAddress!;

            List<Value> requiredAmount = [];
            int outputIndex = 0;
            foreach (var config in _outputConfigs)
            {
                var outputOptions = new OutputOptions("", null, null);
                config(outputOptions, param);

                if (
                    !string.IsNullOrEmpty(outputOptions.AssociatedInputId) &&
                    !string.IsNullOrEmpty(outputOptions.Id) &&
                    associationsByInputId.TryGetValue(outputOptions.AssociatedInputId, out var associations))
                {
                    associations[outputOptions.Id] = outputIndex;
                }

                requiredAmount.Add(outputOptions.Amount!);
                txBuilder.AddOutput(OutputUtils.BuildOutput(outputOptions, parties));

                if (isSmartContractTx && !string.IsNullOrEmpty(outputOptions.To))
                {
                    changeAddress = ChrysalisWallet.Address.FromBech32(parties[outputOptions.To]);
                }

                changeIndex++;
                outputIndex++;
            }

            List<ResolvedInput> utxos = await _provider!.GetUtxosAsync(senderAddress!.ToBech32());

            byte[] scriptCborBytes = [];
            if (isSmartContractTx && referenceInput != null)
            {
                foreach (var utxo in utxos)
                {
                    if (Convert.ToHexString(utxo.Outref.TransactionId) == Convert.ToHexString(referenceInput.TransactionId) &&
                        utxo.Outref.Index == referenceInput.Index)
                    {
                        scriptCborBytes = utxo.Output switch
                        {
                            PostAlonzoTransactionOutput postAlonzoOutput =>
                                postAlonzoOutput.ScriptRef?.Value ?? Array.Empty<byte>(),
                            _ => throw new InvalidOperationException($"Invalid output type: {utxo.Output.GetType().Name}")
                        };
                        break;
                    }
                }
            }

            var allUtxos = new List<ResolvedInput>(utxos);

            if (senderAddress!.ToBech32() != changeAddress.ToBech32())
            {
                List<ResolvedInput> changeAddressUtxos = await _provider!.GetUtxosAsync(changeAddress.ToBech32());
                allUtxos.AddRange(changeAddressUtxos);
                utxos = changeAddressUtxos;
            }

            ResolvedInput? feeInput = utxos
                .Where(e => e.Output.Amount().Lovelace() >= 5_000_000UL && e.Output.Amount() is Lovelace)
                .OrderByDescending(e => e.Output.Amount().Lovelace())
                .FirstOrDefault();

            if (feeInput is not null)
            {
                utxos.Remove(feeInput);
                txBuilder.AddInput(feeInput.Outref);
            }

            ResolvedInput? collateralInput = utxos
                .Where(e => e.Output.Amount().Lovelace() >= 5_000_000UL && e.Output.Amount() is Lovelace)
                .OrderByDescending(e => e.Output.Amount().Lovelace())
                .FirstOrDefault();

            if (collateralInput is not null && isSmartContractTx)
            {
                utxos.Remove(collateralInput);
                txBuilder.SetCollateral(collateralInput);
            }

            List<ResolvedInput> specifiedInputsUtxos = [];
            foreach (var input in specifiedInputs)
            {
                ResolvedInput specifiedInputUtxo = allUtxos.First(e =>
                    Convert.ToHexString(e.Outref.TransactionId) == Convert.ToHexString(input.TransactionId) &&
                    e.Outref.Index == input.Index);

                specifiedInputsUtxos.Add(specifiedInputUtxo);
                utxos.Remove(specifiedInputUtxo);
            }

            var coinSelectionResult = CoinSelectionAlgorithm.LargestFirstAlgorithm(
                utxos,
                requiredAmount,
                specifiedInputs: specifiedInputsUtxos,
                minimumAmount: new Lovelace(minimumLovelace)
            );

            foreach (var consumedInput in coinSelectionResult.Inputs)
            {
                txBuilder.AddInput(consumedInput.Outref);
            }

            ulong totalLovelaceChange = coinSelectionResult.LovelaceChange;
            Dictionary<byte[], TokenBundleOutput> assetsChange = coinSelectionResult.AssetsChange;

            var lovelaceChange = new Lovelace(totalLovelaceChange + (feeInput?.Output.Amount().Lovelace() ?? 0));
            Value changeValue = lovelaceChange;

            if (assetsChange.Count > 0)
            {
                changeValue = new LovelaceWithMultiAsset(lovelaceChange, new MultiAssetOutput(assetsChange));
            }

            var changeOutput = new AlonzoTransactionOutput(
                new Address(changeAddress.ToBytes()),
                changeValue,
                null
            );

            txBuilder.AddOutput(changeOutput, true);

            List<TransactionInput> sortedInputs = [.. txBuilder.body.Inputs.Value().OrderBy(e => Convert.ToHexString(e.TransactionId)).ThenBy(e => e.Index)];

            txBuilder.SetInputs(sortedInputs);

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

            Dictionary<int, Dictionary<string, int>> indexedAssociations = [];
            foreach (var (inputId, roleToOutputIndex) in associationsByInputId)
            {
                if (!inputIdToOrderedIndex.TryGetValue(inputId, out int inputIndex))
                    continue;

                indexedAssociations[inputIndex] = new Dictionary<string, int>(roleToOutputIndex);
            }

            Dictionary<RewardAccount, ulong> rewards = [];
            foreach (var config in _withdrawalConfigs)
            {
                var withdrawalOptions = new WithdrawalOptions<T>("", 0);
                config(withdrawalOptions, param);

                ChrysalisWallet.Address withdrawalAddress = ChrysalisWallet.Address.FromBech32(parties[withdrawalOptions.From]);
                rewards.Add(new RewardAccount(withdrawalAddress.ToBytes()), withdrawalOptions.Amount!);

                if (withdrawalOptions.Redeemers is not null)
                {
                    foreach (var kvp in withdrawalOptions.Redeemers.Value)
                    {
                        redeemers[kvp.Key] = kvp.Value;
                    }
                    continue;
                }

                if (withdrawalOptions.RedeemerGenerator is not null)
                {
                    withdrawalOptions.RedeemerGenerator(indexedAssociations, param, redeemers);
                }
            }

            if (rewards.Count > 0)
            {
                txBuilder.SetWithdrawals(new Withdrawals(rewards));
            }

            if (isSmartContractTx)
            {
                txBuilder.SetRedeemers(new RedeemerMap(redeemers));
                //@TODO: Uncomment when Evaluate is fixed
                // txBuilder.Evaluate(allUtxos); 
            }

            PostMaryTransaction unsignedTx = txBuilder
                .CalculateFee(scriptCborBytes)
                .Build();

            return unsignedTx;
        };
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
}