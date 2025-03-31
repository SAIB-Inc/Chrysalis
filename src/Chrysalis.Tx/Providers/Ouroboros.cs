using Chrysalis.Cbor.Types.Cardano.Core.Protocol;
using CborTransactionInput = Chrysalis.Cbor.Types.Cardano.Core.Transaction.TransactionInput;
using Chrysalis.Network.Cbor.Handshake;
using Chrysalis.Network.Cbor.LocalStateQuery;
using Chrysalis.Network.MiniProtocols.Extensions;
using Chrysalis.Network.Multiplexer;
using Chrysalis.Tx.Models;
using Chrysalis.Wallet.Models.Addresses;
using Chrysalis.Cbor.Types.Cardano.Core.Transaction;
using Chrysalis.Cbor.Extensions.Cardano.Core.Transaction;
using Chrysalis.Cbor.Types;
using Chrysalis.Tx.Extensions;

namespace Chrysalis.Tx.Providers;
public class Ouroboros(string socketPath) : ICardanoDataProvider
{
    private readonly string _socketPath = socketPath ?? throw new ArgumentNullException(nameof(socketPath));

    public async Task<ConwayProtocolParamUpdate> GetProtocolParametersAsync()
    {
        NodeClient client = await NodeClient.ConnectAsync(_socketPath);
        client.Start();

        ProposeVersions proposeVersion = HandshakeMessages.ProposeVersions(VersionTables.N2C_V10_AND_ABOVE);
        await client.Handshake!.SendAsync(proposeVersion, CancellationToken.None);

        CurrentProtocolParamsResponse currentProtocolParams = await client.LocalStateQuery!.GetCurrentProtocolParamsAsync();

        return currentProtocolParams.ProtocolParams.Conway();
    }

    public async Task<List<ResolvedInput>> GetUtxosByAddressAsync(List<string> bech32Address)
    {
        NodeClient client = await NodeClient.ConnectAsync(_socketPath);
        client.Start();

        ProposeVersions proposeVersion = HandshakeMessages.ProposeVersions(VersionTables.N2C_V10_AND_ABOVE);
        await client.Handshake!.SendAsync(proposeVersion, CancellationToken.None);

        UtxoByAddressResponse utxos = await client.LocalStateQuery!.GetUtxosByAddressAsync(bech32Address.Select(x => Address.FromBech32(x).ToBytes()).ToList());

        List<ResolvedInput> resolvedInputs = [];
        foreach (var (key, value) in utxos.Utxos)
        {
            byte[] txHash = key.TxHash;
            ulong index = key.Index;

            TransactionOutput output = new PostAlonzoTransactionOutput(
                new Cbor.Types.Cardano.Core.Common.Address(value.Address()),
                value.Amount(),
                value.DatumOption(),
                value.ScriptRef() is not null ? new CborEncodedValue(value.ScriptRef()!) : null
            );

            resolvedInputs.Add(new ResolvedInput(new CborTransactionInput(txHash, index), output));
            
        }
        return resolvedInputs;
    }

}