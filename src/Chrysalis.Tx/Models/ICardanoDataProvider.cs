using Chrysalis.Cbor.Types.Cardano.Core.Protocol;
using Chrysalis.Cbor.Types.Cardano.Core.Transaction;

namespace Chrysalis.Tx.Models;

public interface ICardanoDataProvider
{
    public Task<List<ResolvedInput>> GetUtxosByAddressAsync(List<string> addresses);
    public Task<ConwayProtocolParamUpdate> GetProtocolParametersAsync();    // Todo: Add more methods
}
