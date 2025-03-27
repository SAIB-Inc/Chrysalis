using System.Runtime.CompilerServices;
using Chrysalis.Cbor.Types.Cardano.Core.Certificates;
using Chrysalis.Cbor.Types.Cardano.Core.Common;
using Chrysalis.Cbor.Types.Cardano.Core.Protocol;
using Chrysalis.Cbor.Types.Cardano.Core.Transaction;
using Chrysalis.Cbor.Types.Cardano.Core.TransactionWitness;
using Chrysalis.Tx.Extensions;
using Chrysalis.Tx.Models;
using Chrysalis.Tx.TransactionBuilding.Extensions;
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
    private readonly List<Action<MintOptions, T>> _mintConfigs = [];
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
            ConwayProtocolParamUpdate pparams = await _provider!.GetParametersAsync();
            TransactionBuilder txBuilder = TransactionBuilder.Create(pparams);

            int changeIndex = 0;

            Dictionary<string, (string address, bool isChange)> additionalParties = param is ITransactionParameters parameters
                ? parameters.Parties ?? []
                : [];

            Dictionary<string, string> parties = MergeParties(_staticParties, additionalParties);

            Dictionary<string, TransactionInput> inputsById = [];
            Dictionary<string, Dictionary<string, int>> associationsByInputId = [];

            if (_changeAddress is null)
            {
                throw new InvalidOperationException("Change address not set");
            }

            ChrysalisWallet.Address changeAddress = ChrysalisWallet.Address.FromBech32(parties[_changeAddress]);

            List<string> inputAddresses = [];
            List<TransactionInput> specifiedInputs = [];
            TransactionInput? referenceInput = null;
            bool isSmartContractTx = false;
            ulong minimumLovelace = 0;
            Dictionary<RedeemerKey, RedeemerValue> redeemers = [];

            foreach (var config in _inputConfigs)
            {
                var inputOptions = new InputOptions<T>("", null, null, null, null, null, null);
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
                        txBuilder.AddInput(inputOptions.UtxoRef);

                        if (inputOptions.Redeemer is not null)
                        {
                            foreach (var kvp in inputOptions.Redeemer.Value)
                            {
                                redeemers[kvp.Key] = kvp.Value;
                            }

                        }
                    }
                }

                if (inputOptions.MinAmount is not null)
                {
                    minimumLovelace += inputOptions.MinAmount.Lovelace();
                }

                if (!string.IsNullOrEmpty(inputOptions.From))
                {
                    inputAddresses.Add(inputOptions.From);
                }
            }

            foreach (var config in _mintConfigs)
            {
                var mintOptions = new MintOptions("", []);
                config(mintOptions, param);

                foreach (var (assetName, amount) in mintOptions.Assets)
                {
                    txBuilder.AddMint(new MultiAssetMint(new Dictionary<byte[], TokenBundleMint>
                    {
                        { Convert.FromHexString(mintOptions.Policy), new TokenBundleMint(new Dictionary<byte[], long> { { Convert.FromHexString(assetName), (long)amount } }) }
                    }));
                }
            }

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
                txBuilder.AddOutput(outputOptions.BuildOutput(parties));

                changeIndex++;
                outputIndex++;
            }

            List<ResolvedInput> utxos = await _provider!.GetUtxosAsync(parties[_changeAddress]);

            var allUtxos = new List<ResolvedInput>(utxos);

            foreach (string address in inputAddresses.Distinct())
            {
                Console.WriteLine(changeAddress);
                if (address != _changeAddress)
                {
                    var addressUtxos = await _provider!.GetUtxosAsync(parties[address]);
                    allUtxos.AddRange(addressUtxos);
                }
            }

            byte[] scriptCborBytes = [];
            if (isSmartContractTx && referenceInput != null)
            {
                foreach (var utxo in allUtxos)
                {
                    if (Convert.ToHexString(utxo.Outref.TransactionId) == Convert.ToHexString(referenceInput.TransactionId) &&
                        utxo.Outref.Index == referenceInput.Index)
                    {
                        scriptCborBytes = utxo.Output switch
                        {
                            PostAlonzoTransactionOutput postAlonzoOutput =>
                                postAlonzoOutput.ScriptRef?.Value ?? [],
                            _ => throw new InvalidOperationException($"Invalid output type: {utxo.Output.GetType().Name}")
                        };
                        break;
                    }
                }
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


            var coinSelectionResult = CoinSelectionUtil.LargestFirstAlgorithm(
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
                var withdrawalOptions = new WithdrawalOptions<T>("", 0, null, null);
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

                if (withdrawalOptions.RedeemerBuilder is not null)
                {
                    withdrawalOptions.RedeemerBuilder(indexedAssociations, param, redeemers);
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
        Dictionary<string, (string address, bool isChange)> dynamicParties)
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
}