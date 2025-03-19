using Chrysalis.Tx.Models;
using Chrysalis.Tx.Builder;
using Chrysalis.Tx.Utils;
using Chrysalis.Cbor.Serialization;
using Chrysalis.Cbor.Cardano.Types.Block.Transaction.Output;

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
            transaction.SetAvailableUtxos(utxos);

            // Set change address (defaults to sender)
            // Address utility is not yet implemented so hardcoded for now
            string changeAddr = "005c5c318d01f729e205c95eb1b02d623dd10e78ea58f72d0c13f892b2e8904edc699e2f0ce7b72be7cec991df651a222e2ae9244eb5975cba";
            transaction.SetChangeAddress(new Address(Convert.FromHexString(changeAddr)));


            var additionalParties = param is IParameters parameters
                ? parameters.Parties ?? []
                : [];

            var parties = MergeParties(staticParties, additionalParties);


            foreach (var config in outputConfigs)
            {
                var outputOptions = new OutputOptions("", null, null);
                config(outputOptions, param);
                transaction.AddOutput(OutputUtils.BuildOutput(outputOptions, parties));
                transaction.SetCollateralReturn(OutputUtils.BuildOutput(outputOptions, parties));
            }

            transaction.CoinSelection();

            foreach (var config in inputConfigs)
            {
                var inputOptions = new InputOptions("", null, null, null, null);
                config(inputOptions, param);
                // Hardcoded for now since coin selection is not yet implemented

                transaction.AddInput(InputUtils.BuildInput("1aef0ed0ac1acc12b38a5e32be81478d4658f3cc92832c6b7bf264005d4a6d10", 1UL));

            }

            transaction.SetFee(2000000UL);

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
