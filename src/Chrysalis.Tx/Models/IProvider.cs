
namespace Chrysalis.Tx.Models;

public interface IProvider
{
    public Task<List<Utxo>> GetUtxosAsync(string address);
    // Todo: Add more methods
}
