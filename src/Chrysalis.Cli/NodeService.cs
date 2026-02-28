
using Chrysalis.Network.Cbor.Common;
using Chrysalis.Network.Cbor.LocalStateQuery;
using Chrysalis.Network.MiniProtocols.Extensions;
using Chrysalis.Network.Multiplexer;
using Chrysalis.Wallet.Models.Addresses;

namespace Chrysalis.Cli;

/// <summary>
/// Provides high-level methods for interacting with a Cardano node via a Unix socket.
/// </summary>
internal sealed class NodeService(string socketPath)
{
    private readonly string _socketPath = socketPath;

    /// <summary>
    /// Retrieves the current chain tip from the connected Cardano node.
    /// </summary>
    /// <returns>The current chain tip.</returns>
    public async Task<Tip> GetTipAsync()
    {
        NodeClient client = await NodeClient.ConnectAsync(_socketPath).ConfigureAwait(false);
        await client.StartAsync().ConfigureAwait(false);
        Tip tip = await client.LocalStateQuery.GetTipAsync().ConfigureAwait(false);
        return tip;
    }

    /// <summary>
    /// Retrieves the current protocol parameters from the connected Cardano node.
    /// </summary>
    /// <returns>The current protocol parameters.</returns>
    public async Task<ProtocolParams> GetCurrentProtocolParamsAsync()
    {
        NodeClient client = await NodeClient.ConnectAsync(_socketPath).ConfigureAwait(false);
        await client.StartAsync().ConfigureAwait(false);
        CurrentProtocolParamsResponse currentProtocolParams = await client.LocalStateQuery.GetCurrentProtocolParamsAsync().ConfigureAwait(false);
        return currentProtocolParams.ProtocolParams;
    }

    /// <summary>
    /// Retrieves the UTxO set for a given address from the connected Cardano node.
    /// </summary>
    /// <param name="address">The Bech32-encoded address to query UTxOs for.</param>
    /// <returns>The UTxO set for the specified address.</returns>
    public async Task<UtxoByAddressResponse> GetUtxoByAddressAsync(string address)
    {
        NodeClient client = await NodeClient.ConnectAsync(_socketPath).ConfigureAwait(false);
        await client.StartAsync().ConfigureAwait(false);
        Address addr = new(address);
        UtxoByAddressResponse utxos = await client.LocalStateQuery.GetUtxosByAddressAsync([addr.ToBytes()]).ConfigureAwait(false);
        return utxos;
    }
}
