
using Chrysalis.Network.Cbor.Common;
using Chrysalis.Network.Cbor.LocalStateQuery;
using Chrysalis.Network.MiniProtocols.Extensions;
using Chrysalis.Network.Multiplexer;
using Chrysalis.Wallet.Models.Addresses;

namespace Chrysalis.Cli;

public class NodeService(string socketPath)
{
    private readonly string _socketPath = socketPath;

    public async Task<Tip> GetTipAsync()
    {
        NodeClient client = await NodeClient.ConnectAsync(_socketPath);
        await client.StartAsync();
        Tip tip = await client.LocalStateQuery.GetTipAsync();
        return tip;
    }

    public async Task<ProtocolParams> GetCurrentProtocolParamsAsync()
    {
        NodeClient client = await NodeClient.ConnectAsync(_socketPath);
        await client.StartAsync();
        CurrentProtocolParamsResponse currentProtocolParams = await client.LocalStateQuery.GetCurrentProtocolParamsAsync();
        return currentProtocolParams.ProtocolParams;
    }

    public async Task<UtxoByAddressResponse> GetUtxoByAddressAsync(string address)
    {
        NodeClient client = await NodeClient.ConnectAsync(_socketPath);
        await client.StartAsync();
        Address addr = new(address);
        UtxoByAddressResponse utxos = await client.LocalStateQuery.GetUtxosByAddressAsync([addr.ToBytes()]);
        return utxos;
    }

}

