using Chrysalis.Cbor.Types.Cardano.Core.Protocol;
using Chrysalis.Cbor.Types.Cardano.Core.Transaction;

namespace Chrysalis.Tx.Models;

public interface ICardanoDataProvider
{
    public Task<List<ResolvedInput>> GetUtxosAsync(List<string> address);
    public Task<ConwayProtocolParamUpdate> GetParametersAsync();
    public Task<string> SubmitTransactionAsync(Transaction tx);
}
