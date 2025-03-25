using Chrysalis.Tx.Models;
using Chrysalis.Tx.TransactionBuilding;
using Chrysalis.Tx.Utils;
using Chrysalis.Cbor.Serialization;
using Chrysalis.Cbor.Cardano.Types.Block.Transaction.Output;
using Chrysalis.Cbor.Cardano.Extensions;
using Chrysalis.Cbor.Types.Primitives;
using TxAddr = Chrysalis.Tx.Models.Addresses;
using Chrysalis.Tx.TransactionBuilding.Extensions;
using Chrysalis.Cbor.Cardano.Types.Block.Transaction;
using Chrysalis.Cbor.Cardano.Types.Block.Transaction.Input;
using Chrysalis.Cbor.Cardano.Types.Block.Transaction.Body.ProposalProcedures;
using Chrysalis.Cbor.Cardano.Types.Block.Transaction.Body;
using Chrysalis.Cbor.Cardano.Types.Block.Transaction.WitnessSet;

namespace Chrysalis.Tx.TemplateBuilder;

public class TxTemplateBuilder<T>
{
    private IProvider? provider;
    private readonly List<Action<InputOptions, T>> inputConfigs = [];
    private readonly List<Action<OutputOptions, T>> outputConfigs = [];
    private readonly Dictionary<string, string> staticParties = [];

    private readonly List<Action<WithdrawalOptions, T>> withdrawalConfigs = [];

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

    public TxTemplateBuilder<T> AddWithdrawal(Action<WithdrawalOptions, T> config)
    {
        withdrawalConfigs.Add(config);
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

            // Get available UTXOs
            // var utxos = await provider!.GetUtxosAsync(senderAddress);

            var pparams = await provider!.GetParametersAsync();
            var txBuilder = TransactionBuilder.Create(pparams);

            int changeIndex = 0;

            var additionalParties = param is IParameters parameters
                ? parameters.Parties ?? []
                : [];

            var parties = MergeParties(staticParties, additionalParties);

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
                minimumLovelace += inputOptions.MinAmount!.Lovelace() ?? 0;
                senderAddress = TxAddr.Address.FromBech32(parties[inputOptions.From]);
            }

            TxAddr.Address changeAddress = senderAddress!;
            List<Value> requiredAmount = [];
            foreach (var config in outputConfigs)
            {
                var outputOptions = new OutputOptions("", null, null);
                config(outputOptions, param);
                requiredAmount.Add(outputOptions.Amount!);
                txBuilder.AddOutput(OutputUtils.BuildOutput(outputOptions, parties));
                if (isSmartContractTx)
                {
                    changeAddress = TxAddr.Address.FromBech32(parties[outputOptions.To]);
                }
                changeIndex++;
            }

            List<ResolvedInput> utxos = await provider!.GetUtxosAsync(senderAddress!.ToBech32());
            byte[] scriptCborBytes = [];
            if (isSmartContractTx)
            {
                foreach (var utxo in utxos)
                {
                    if (Convert.ToHexString(utxo.Outref.TransactionId.Value) == Convert.ToHexString(referenceInput!.TransactionId.Value) && utxo.Outref.Index == referenceInput!.Index)
                    {
                        scriptCborBytes = utxo!.Output.ScriptRef()!;
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
            foreach (var utxo in utxos.OrderByDescending(e => e.Output.Amount()!.Lovelace()))
            {
                if (utxo.Output.Amount()!.Lovelace() >= 5000000UL && utxo.Output.Amount() is Lovelace)
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
            foreach (var utxo in utxos.OrderByDescending(e => e.Output.Amount()!.Lovelace()))
            {
                if (utxo.Output.Amount()!.Lovelace() >= 5000000UL && utxo.Output.Amount() is Lovelace)
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


            ulong totalChange = 0;
            Dictionary<CborBytes, TokenBundleOutput> assetsChange = [];
            if (!isSmartContractTx)
            {
                var coinSelectionResult = CoinSelectionAlgorithm.LargestFirstAlgorithm(utxos, requiredAmount, minimumAmount: new Lovelace(minimumLovelace));
                foreach (var consumed_input in coinSelectionResult.Inputs)
                {
                    txBuilder.AddInput(consumed_input.Outref);
                }
                totalChange = coinSelectionResult.LovelaceChange;
                assetsChange = coinSelectionResult.AssetsChange;
            }


            var lovelaceChange = new Lovelace(totalChange + feeInput?.Output.Amount()!.Lovelace() ?? 0);
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

            Dictionary<RewardAccount, CborUlong> rewards = [];
            foreach (var config in withdrawalConfigs)
            {
                var withdrawalOptions = new WithdrawalOptions("", 0, null);
                config(withdrawalOptions, param);
                TxAddr.Address withdrawalAddress = TxAddr.Address.FromBech32(parties[withdrawalOptions.From]);
                rewards.Add(new RewardAccount(withdrawalAddress.ToBytes()), new CborUlong(withdrawalOptions.Amount!));
                switch (withdrawalOptions.Redeemer)
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

            if (rewards.Count > 0)
            {
                txBuilder.SetWithdrawals(new Withdrawals(rewards));
            }

            if (isSmartContractTx)
            {
                txBuilder.SetRedeemers(new RedeemerMap(redeemers));
                txBuilder.Evaluate(allUtxos);
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
