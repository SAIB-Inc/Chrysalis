using Chrysalis.Tx.Models;
using Chrysalis.Tx.TransactionBuilding;
using Chrysalis.Tx.Utils;
using Chrysalis.Cbor.Cardano.Types.Block.Transaction.Output;
using TxAddr = Chrysalis.Tx.Models.Addresses;
using Chrysalis.Tx.TransactionBuilding.Extensions;
using Chrysalis.Cbor.Cardano.Types.Block.Transaction;
using Chrysalis.Cbor.Cardano.Types.Block.Transaction.Input;
using Chrysalis.Cbor.Cardano.Types.Block.Transaction.Body.ProposalProcedures;
using Chrysalis.Cbor.Cardano.Types.Block.Transaction.Body;
using Chrysalis.Cbor.Cardano.Types.Block.Transaction.WitnessSet;
using Chrysalis.Tx.Extensions;

namespace Chrysalis.Tx.TemplateBuilder;

public class TxTemplateBuilder<T>
{
    private IProvider? provider;
    private readonly List<Action<InputOptions, T>> inputConfigs = [];
    private readonly List<Action<OutputOptions, T>> outputConfigs = [];
    private readonly Dictionary<string, string> staticParties = [];

    public bool trackAssociations = true;
    private Action<Dictionary<int, Dictionary<string, int>>, T, Dictionary<RedeemerKey, RedeemerValue>>? redeemerGenerator;

    private readonly List<Action<WithdrawalOptions<T>, T>> withdrawalConfigs = [];

    public static TxTemplateBuilder<T> Create(IProvider provider) => new TxTemplateBuilder<T>().SetProvider(provider);

    private TxTemplateBuilder<T> SetProvider(IProvider provider)
    {
        this.provider = provider;
        return this;
    }
    public TxTemplateBuilder<T> AddInput(Action<InputOptions, T> config)
    {
        inputConfigs.Add(config);
        return this;
    }

    public TxTemplateBuilder<T> AddOutput(Action<OutputOptions, T> config)
    {
        outputConfigs.Add(config);
        return this;
    }

    public TxTemplateBuilder<T> AddWithdrawal(Action<WithdrawalOptions<T>, T> config)
    {
        withdrawalConfigs.Add(config);
        return this;
    }

    public TxTemplateBuilder<T> SetRedeemerGenerator(Action<Dictionary<int, Dictionary<string, int>>, T, Dictionary<RedeemerKey, RedeemerValue>>? generator)
    {
        redeemerGenerator = generator;
        return this;
    }
    public TxTemplateBuilder<T> AddStaticParty(string partyIdent, string party)
    {
        staticParties[partyIdent] = party;
        return this;
    }


    public Func<T, Task<PostMaryTransaction>> Build()
    {
        return async param =>
        {
            var pparams = await provider!.GetParametersAsync();
            var txBuilder = TransactionBuilder.Create(pparams);

            int changeIndex = 0;

            var additionalParties = param is IParameters parameters
                ? parameters.Parties ?? []
                : [];

            var parties = MergeParties(staticParties, additionalParties);

            Dictionary<string, TransactionInput> inputsById = [];
            Dictionary<string, Dictionary<string, int>> associationsByInputId = [];

            TxAddr.Address? senderAddress = null;
            List<TransactionInput> specifiedInputs = [];
            TransactionInput? referenceInput = null;
            bool isSmartContractTx = false;
            ulong minimumLovelace = 0;
            Dictionary<RedeemerKey, RedeemerValue> redeemers = [];
            foreach (var config in inputConfigs)
            {
                var inputOptions = new InputOptions("", null, null, null, null);
                config(inputOptions, param);

                if (trackAssociations && !string.IsNullOrEmpty(inputOptions.Id) && inputOptions.UtxoRef != null)
                {
                    inputsById[inputOptions.Id] = inputOptions.UtxoRef;
                    associationsByInputId[inputOptions.Id] = [];
                }

                if (inputOptions.UtxoRef is not null)
                {
                    if (inputOptions.IsReference)
                    {
                        txBuilder.AddReferenceInput(inputOptions.UtxoRef!);
                        referenceInput = inputOptions.UtxoRef;
                        isSmartContractTx = true;
                        continue;
                    }
                    else
                    {
                        specifiedInputs.Add(inputOptions.UtxoRef);
                        txBuilder.AddInput(inputOptions.UtxoRef);
                        if (inputOptions.Redeemer is not null)
                        {
                            switch (inputOptions.Redeemer)
                            {
                                case RedeemerMap redeemersMap:
                                    foreach (var kvp in redeemersMap.Value)
                                    {
                                        redeemers[kvp.Key] = kvp.Value;
                                    }
                                    break;
                                case RedeemerList redeemersList:
                                    foreach (var redeemer in redeemersList.Value)
                                    {
                                        redeemers[new RedeemerKey(redeemer.Tag, redeemer.Index)] = new RedeemerValue(redeemer.Data, redeemer.ExUnits);
                                    }
                                    break;
                            }
                        }
                    }

                }
                if (inputOptions.MinAmount is not null)
                {
                    _ = inputOptions.MinAmount switch
                    {
                        Lovelace lovelace => minimumLovelace += lovelace.Value,
                        LovelaceWithMultiAsset multiAsset => minimumLovelace += multiAsset.LovelaceValue.Value,
                        _ => throw new Exception("Invalid value type")
                    };
                }

                senderAddress = TxAddr.Address.FromBech32(parties[inputOptions.From]);
            }

            TxAddr.Address changeAddress = senderAddress!;

            List<Value> requiredAmount = [];
            int outputIndex = 0;
            foreach (var config in outputConfigs)
            {
                var outputOptions = new OutputOptions("", null, null);
                config(outputOptions, param);
                if (trackAssociations &&
                    !string.IsNullOrEmpty(outputOptions.AssociatedInputId) &&
                    !string.IsNullOrEmpty(outputOptions.Id) &&
                    associationsByInputId.TryGetValue(outputOptions.AssociatedInputId, out var associations))
                {
                    associations[outputOptions.Id] = outputIndex;
                }
                requiredAmount.Add(outputOptions.Amount!);
                txBuilder.AddOutput(OutputUtils.BuildOutput(outputOptions, parties));
                if (isSmartContractTx)
                {
                    changeAddress = TxAddr.Address.FromBech32(parties[outputOptions.To]);
                }
                changeIndex++;
                outputIndex++;
            }

            List<ResolvedInput> utxos = await provider!.GetUtxosAsync(senderAddress!.ToBech32());

            byte[] scriptCborBytes = [];
            if (isSmartContractTx)
            {
                foreach (var utxo in utxos)
                {
                    if (Convert.ToHexString(utxo.Outref.TransactionId) == Convert.ToHexString(referenceInput!.TransactionId) && utxo.Outref.Index == referenceInput!.Index)
                    {
                        scriptCborBytes = utxo!.Output switch
                        {
                            PostAlonzoTransactionOutput postAlonzoTransactionOutput => postAlonzoTransactionOutput.ScriptRef!.Value,
                            _ => throw new Exception("Invalid output type")
                        };
                        break;
                    }
                }
            }


            var allUtxos = new List<ResolvedInput>(utxos);

            if (senderAddress!.ToBech32() != changeAddress.ToBech32())
            {
                List<ResolvedInput> changeAdressUtxos = await provider!.GetUtxosAsync(changeAddress.ToBech32());
                allUtxos.AddRange(changeAdressUtxos);
                utxos = changeAdressUtxos;
            }

            ResolvedInput? feeInput = null;
            foreach (var utxo in utxos.OrderByDescending(e => e.Output.Amount().Lovelace()))
            {
                if (utxo.Output.Amount().Lovelace() >= 5000000UL && utxo.Output.Amount() is Lovelace)
                {
                    feeInput = utxo;
                    break;
                }
            }

            if (feeInput is not null)
            {
                utxos.Remove(feeInput);
                txBuilder.AddInput(feeInput.Outref);
            }

            ResolvedInput? collateralInput = null;
            foreach (var utxo in utxos.OrderByDescending(e => e.Output.Amount().Lovelace()))
            {
                if (utxo.Output.Amount().Lovelace() >= 5000000UL && utxo.Output.Amount() is Lovelace)
                {
                    collateralInput = utxo;
                    break;
                }
            }

            if (collateralInput is not null && isSmartContractTx)
            {
                utxos.Remove(collateralInput);
                txBuilder.SetCollateral(collateralInput);
            }

            List<ResolvedInput> specifiedInputsUtxos = [];
            foreach (var utxo in specifiedInputs)
            {
                ResolvedInput specifiedInputUtxo = allUtxos.First(e => Convert.ToHexString(e.Outref.TransactionId) == Convert.ToHexString(utxo.TransactionId) && e.Outref.Index == utxo.Index);
                specifiedInputsUtxos.Add(specifiedInputUtxo);
                utxos.Remove(specifiedInputUtxo);
            }

            var coinSelectionResult = CoinSelectionAlgorithm.LargestFirstAlgorithm(utxos, requiredAmount, specifiedInputs: specifiedInputsUtxos, minimumAmount: new Lovelace(minimumLovelace));
            foreach (var consumed_input in coinSelectionResult.Inputs)
            {

                txBuilder.AddInput(consumed_input.Outref);
            }
            ulong totalLovelaceChange = coinSelectionResult.LovelaceChange;
            Dictionary<byte[], TokenBundleOutput> assetsChange = coinSelectionResult.AssetsChange;

            var lovelaceChange = new Lovelace(totalLovelaceChange + feeInput?.Output.Amount().Lovelace() ?? 0);
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


            txBuilder.bodyBuilder.Inputs = [.. txBuilder.bodyBuilder.Inputs
                .OrderBy(e => Convert.ToHexString(e.TransactionId))
                .ThenBy(e => e.Index)];

            List<TransactionInput> sortedInputs = txBuilder.bodyBuilder.Inputs;

            Dictionary<string, int> inputIdToOrderedIndex = [];

            foreach (var kvp in inputsById)
            {
                string inputId = kvp.Key;
                TransactionInput input = kvp.Value;

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

            foreach (var kvp in associationsByInputId)
            {
                string inputId = kvp.Key;
                Dictionary<string, int> roleToOutputIndex = kvp.Value;

                if (!inputIdToOrderedIndex.TryGetValue(inputId, out int inputIndex))
                    continue;

                indexedAssociations[inputIndex] = new Dictionary<string, int>(roleToOutputIndex);
            }

            Dictionary<RewardAccount, ulong> rewards = [];
            foreach (var config in withdrawalConfigs)
            {
                var withdrawalOptions = new WithdrawalOptions<T>("", 0);
                config(withdrawalOptions, param);
                TxAddr.Address withdrawalAddress = TxAddr.Address.FromBech32(parties[withdrawalOptions.From]);
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
        foreach (var kvp in dynamicParties)
        {
            result[kvp.Key] = kvp.Value;
        }
        return result;
    }

}
