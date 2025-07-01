using Chrysalis.Cbor.Types.Cardano.Core.Transaction;
using Chrysalis.Network.Cbor.LocalStateQuery;
using Chrysalis.Tx.Models.Cbor;
using Chrysalis.Cbor.Types.Cardano.Core;
using Chrysalis.Wallet.Models.Enums;

namespace Chrysalis.Tx.Models;

public interface ICardanoDataProvider
{
    public Task<List<ResolvedInput>> GetUtxosAsync(List<string> address);
    public Task<ProtocolParams> GetParametersAsync();
    public Task<string> SubmitTransactionAsync(Transaction tx);
    public Task<Metadata?> GetTransactionMetadataAsync(string txHash);
    public NetworkType NetworkType { get; }
}
