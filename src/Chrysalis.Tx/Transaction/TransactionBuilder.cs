using Chrysalis.Tx.Models;

namespace Chrysalis.Tx.Transaction;

public class TransactionBuilder
{

    public readonly List<Address> inputs = new();
    public readonly List<Address> outputs = new();
    public TransactionBuilder AddInput(Address address, Value? amount)
    {
        inputs.Add(address);
        return this;
    }

    public TransactionBuilder AddOutput(Address address, Value? amount)
    {
        outputs.Add(address);
        return this;
    }

    public string Build()
    {
        return "unsigned tx";
    }


}