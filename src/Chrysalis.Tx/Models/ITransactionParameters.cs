namespace Chrysalis.Tx.Models;
public interface ITransactionParameters
{
    Dictionary<string, (string address, bool isChange)> Parties { get; set; }
}