using Chrysalis.Cbor.Types.Cardano.Core.Transaction;
using Chrysalis.Network.Cbor.LocalStateQuery;
using Chrysalis.Tx.Models.Cbor;

namespace Chrysalis.Tx.Models;

public interface ICardanoDataProvider
{
    public Task<List<ResolvedInput>> GetUtxosAsync(List<string> address);
    public Task<ProtocolParams> GetParametersAsync();
    public Task<string> SubmitTransactionAsync(Transaction tx);
}
