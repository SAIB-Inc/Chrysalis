using Chrysalis.Tx.Models;
using Chrysalis.Tx.TransactionBuilding;
using Chrysalis.Tx.Utils;
using Chrysalis.Cbor.Serialization;
using Chrysalis.Cbor.Cardano.Types.Block.Transaction.Output;
using Chrysalis.Cbor.Cardano.Extensions;
using Chrysalis.Cbor.Types.Primitives;

namespace Chrysalis.Tx.TemplateBuilder;

public class TxTemplateBuilder<T>
{
    private IProvider? provider;
    private readonly List<Action<InputOptions, T>> inputConfigs = [];
    private readonly List<Action<OutputOptions, T>> outputConfigs = [];
    private readonly Dictionary<string, string> staticParties = [];

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
    public TxTemplateBuilder<T> AddStaticParty(string partyIdent, string party)
    {
        staticParties[partyIdent] = party;
        return this;
    }


    public Func<T, Task<byte[]>> Build()
    {
        return async param =>
        {
            var transaction = TransactionBuilder.Create();

            // Extract default sender address
            string senderAddress = "addr_test1qpw9cvvdq8mjncs9e90trvpdvg7azrncafv0wtgvz0uf9vhgjp8dc6v79uxw0detul8vnywlv5dzyt32ayjyadvhtjaqyl2gur";

            // Get available UTXOs
            var utxos = await provider!.GetUtxosAsync(senderAddress);

            var pparams = await provider!.GetParametersAsync();

            string changeAddr = "005c5c318d01f729e205c95eb1b02d623dd10e78ea58f72d0c13f892b2e8904edc699e2f0ce7b72be7cec991df651a222e2ae9244eb5975cba";
            int changeIndex = 0;


            var additionalParties = param is IParameters parameters
                ? parameters.Parties ?? []
                : [];

            var parties = MergeParties(staticParties, additionalParties);

            ulong totalOutputLovelace = 0;
            foreach (var config in outputConfigs)
            {
                var outputOptions = new OutputOptions("", null, null);
                config(outputOptions, param);
                totalOutputLovelace += outputOptions.Amount!.Lovelace() ?? 0;
                transaction.AddOutput(OutputUtils.BuildOutput(outputOptions, parties));
                changeIndex++;
            }

            List<Value> requiredInputValues = [];
            foreach (var config in inputConfigs)
            {
                var inputOptions = new InputOptions("", null, null, null, null);
                config(inputOptions, param);
                requiredInputValues.Add(inputOptions.MinAmount!);
            }


            var coinSelectionResult = CoinSelectionAlgorithm.LargestFirstAlgorithm(utxos, requiredInputValues);

            foreach (var utxo in coinSelectionResult.Inputs)
            {
                transaction.AddInput(utxo.Outref);
            }

            TransactionOutput? changeOutput = null;

            if (coinSelectionResult.LovelaceChange > 0 || coinSelectionResult.AssetsChange.Count > 0)
            {
                Console.WriteLine(coinSelectionResult.LovelaceChange);
                Lovelace lovelaceChange = new(coinSelectionResult.LovelaceChange);
                Value changeValue = lovelaceChange;

                Dictionary<CborBytes, TokenBundleOutput> changeAssets = [];
                foreach (var asset in coinSelectionResult.AssetsChange)
                {
                    var policy = Convert.FromHexString(asset.Key[..56]);
                    var assetName = Convert.FromHexString(asset.Key[56..]);

                    if (!changeAssets.ContainsKey(new CborBytes(policy)))
                    {
                        changeAssets[new CborBytes(policy)] = new TokenBundleOutput(new Dictionary<CborBytes, CborUlong>
                        {
                            [new CborBytes(assetName)] = new CborUlong(asset.Value)
                        });
                    }
                    else
                    {
                        changeAssets[new CborBytes(policy)].Value[new CborBytes(assetName)] = new CborUlong(asset.Value);
                    }
                }
                if (changeAssets.Count > 0)
                {
                    changeValue = new LovelaceWithMultiAsset(lovelaceChange, new MultiAssetOutput(changeAssets));
                }

                changeOutput = new AlonzoTransactionOutput(
                    new Address(Convert.FromHexString(changeAddr)),
                    changeValue,
                    null
                );
            }

            var draftTxBuilder = transaction;

            if (changeOutput is not null)
            {
                draftTxBuilder.AddOutput(changeOutput, true);
            }

            // var draftWitnessSet = WitnessSetUtils.BuildPlaceholderWitnessSet();
            // draftTxBuilder.SetWitnessSet(draftWitnessSet);

            var draftTx = draftTxBuilder.Build();
            var draftTxCbor = CborSerializer.Serialize(draftTx);
            ulong draftTxSize = (ulong)draftTxCbor.Length;

            ulong min_fee = FeeUtil.CalculateFeeWithWitness(draftTxSize, pparams.MinFeeA!.Value, pparams.MinFeeB!.Value, 1);

            if (changeOutput is not null)
            {
                transaction.AddOutput(new AlonzoTransactionOutput(
                    new Address(Convert.FromHexString(changeAddr)),
                    new Lovelace(changeOutput.Amount()!.Lovelace()!.Value! - min_fee),
                    null
                ), false);
            }

            transaction.SetFee(min_fee);
            //Hardcoded for now since fee calculation is not yet implemented

            var tx = transaction.Build();
            return CborSerializer.Serialize(tx);
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
