using Chrysalis.Codec.Extensions.Cardano.Core.Transaction;
using Chrysalis.Codec.Types.Cardano.Core;
using Chrysalis.Codec.Types.Cardano.Core.Protocol;
using Chrysalis.Codec.Types.Cardano.Core.Transaction;
using Chrysalis.Tx.Models;
using Chrysalis.Tx.Models.Cbor;
using Chrysalis.Wallet.Models.Enums;
using WalletAddress = Chrysalis.Wallet.Models.Addresses.Address;

namespace Chrysalis.Tx.Test;

/// <summary>Serves protocol parameters and UTxOs from a CLI snapshot.</summary>
internal sealed class CliSnapshotProvider(ProtocolParams parameters, List<ResolvedInput> inputs, NetworkType network = NetworkType.Preprod) : ICardanoDataProvider
{
    public NetworkType NetworkType => network;
    public Task<ProtocolParams> GetParametersAsync(CancellationToken cancellationToken = default) => Task.FromResult(parameters);
    internal static string Bech32(ITransactionOutput output) => WalletAddress.FromBytes(output.Address().ToArray()).ToBech32();
    public Task<List<ResolvedInput>> GetUtxosAsync(List<string> address, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(address);
        return Task.FromResult(inputs.Where(input => address.Contains(Bech32(input.Output), StringComparer.Ordinal)).ToList());
    }
    public Task<ResolvedInput?> GetUtxoByOutRefAsync(string txHash, ulong index, CancellationToken cancellationToken = default) => Task.FromResult(inputs.FirstOrDefault(input => string.Equals(Convert.ToHexStringLower(input.Outref.TransactionId.Span), txHash, StringComparison.OrdinalIgnoreCase) && input.Outref.Index == index));
    public Task<string> SubmitTransactionAsync(ITransaction tx, CancellationToken cancellationToken = default) => throw new NotSupportedException("Submission is deliberately through the production mini-protocol, not this CLI snapshot adapter.");
    public Task<Metadata?> GetTransactionMetadataAsync(string txHash, CancellationToken cancellationToken = default) => Task.FromResult<Metadata?>(null);
}
