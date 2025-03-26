using Chrysalis.Cbor.Types.Cardano.Core.Protocol;

namespace Chrysalis.Tx.Models;

public interface IProvider
{
    public Task<List<ResolvedInput>> GetUtxosAsync(string address);
    public Task<ConwayProtocolParamUpdate> GetParametersAsync();    // Todo: Add more methods
}
