using Chrysalis.Tx.Models;
using Chrysalis.Tx.Transaction;

namespace Chrysalis.Tx;

public class TxTemplateBuilder<T>
{
    private readonly List<Action<InputOptions, T>> inputConfigs = [];
    private readonly List<Action<OutputOptions, T>> outputConfigs = [];
    private readonly Dictionary<string, Address> parties = [];


    public static TxTemplateBuilder<T> Create() => new();
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
    public TxTemplateBuilder<T> AddParty(string partyIdent, Address party)
    {
        parties[partyIdent] = party;
        return this;
    }

    public Func<T, string> Build()
    {
        return param =>
        {
            var transaction = new TransactionBuilder();
            foreach (var config in inputConfigs)
            {
                var inputOptions = new InputOptions("", null, null, null, null);
                config(inputOptions, param);
                transaction.AddInput(parties.GetValueOrDefault(inputOptions.From)!, inputOptions.MinAmount);
            }
            foreach (var config in outputConfigs)
            {
                var outputOptions = new OutputOptions("", null, null);
                config(outputOptions, param);

                transaction.AddOutput(parties.GetValueOrDefault(outputOptions.To)!, outputOptions.Amount);
            }

            return transaction.Build();
        };
    }

}
