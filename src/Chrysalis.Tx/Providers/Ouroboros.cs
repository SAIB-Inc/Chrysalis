using CborTransactionInput = Chrysalis.Cbor.Types.Cardano.Core.Transaction.TransactionInput;
using Chrysalis.Network.Cbor.LocalStateQuery;
using Chrysalis.Network.MiniProtocols.Extensions;
using Chrysalis.Network.Multiplexer;
using Chrysalis.Tx.Models;
using Chrysalis.Wallet.Models.Addresses;
using Chrysalis.Cbor.Types.Cardano.Core.Transaction;
using Chrysalis.Cbor.Extensions.Cardano.Core.Transaction;
using Chrysalis.Cbor.Types;
using Chrysalis.Network.Cbor.LocalTxSubmit;
using Chrysalis.Cbor.Serialization;
using Chrysalis.Wallet.Utils;
using Chrysalis.Tx.Models.Cbor;
using Chrysalis.Cbor.Types.Cardano.Core;
using Chrysalis.Wallet.Models.Enums;

namespace Chrysalis.Tx.Providers;

public class Ouroboros(string socketPath, ulong networkMagic = 2) : ICardanoDataProvider
{
    private readonly string _socketPath = socketPath ?? throw new ArgumentNullException(nameof(socketPath));
    private readonly ulong _networkMagic = networkMagic;

    public NetworkType NetworkType =>
        _networkMagic switch
        {
            764824073 => NetworkType.Mainnet,
            1 => NetworkType.Preprod,
            _ => NetworkType.Preview
        };

    public static ulong GetNetworkMagic(NetworkType networkType) =>
        networkType switch
        {
            NetworkType.Mainnet => 764824073UL,
            NetworkType.Preprod => 1UL,
            _ => 2UL
        };

    public async Task<ProtocolParams> GetParametersAsync()
    {
        NodeClient client = await NodeClient.ConnectAsync(_socketPath);
        await client.StartAsync(_networkMagic);

        CurrentProtocolParamsResponse currentProtocolParams = await client.LocalStateQuery.GetCurrentProtocolParamsAsync();

        return currentProtocolParams.ProtocolParams;
    }


    public async Task<List<ResolvedInput>> GetUtxosAsync(List<string> bech32Address)
    {
        NodeClient client = await NodeClient.ConnectAsync(_socketPath);
        await client.StartAsync(_networkMagic);

        UtxoByAddressResponse utxos = await client.LocalStateQuery.GetUtxosByAddressAsync(bech32Address.Select(x => Address.FromBech32(x).ToBytes()).ToList());

        List<ResolvedInput> resolvedInputs = [];
        foreach (var (key, value) in utxos.Utxos)
        {
            byte[] txHash = key.TransactionId;
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

    public async Task<string> SubmitTransactionAsync(Transaction tx)
    {
        NodeClient client = await NodeClient.ConnectAsync(_socketPath);
        await client.StartAsync(_networkMagic);

        string txHex = Convert.ToHexString(CborSerializer.Serialize(tx));
        PostMaryTransaction postMaryTx = (PostMaryTransaction)tx;
        byte[] txBody = CborSerializer.Serialize(postMaryTx.TransactionBody);

        EraTx eraTx = new(6, new CborEncodedValue(Convert.FromHexString(txHex)));
        LocalTxSubmissionMessage result = await client.LocalTxSubmit.SubmitTxAsync(new SubmitTx(new Value0(0), eraTx), CancellationToken.None);

        string txHash = result switch
        {
            AcceptTx _ => Convert.ToHexString(HashUtil.Blake2b256(txBody)).ToLowerInvariant(),
            _ => throw new InvalidOperationException("Transaction submission failed")
        };

        return txHash;
    }

    public Task<Metadata?> GetTransactionMetadataAsync(string txHash)
    {
        throw new NotImplementedException("Transaction metadata retrieval is not supported via Ouroboros protocol");
    }
}