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

/// <summary>
/// Ouroboros mini-protocol based implementation of the Cardano data provider.
/// </summary>
/// <param name="socketPath">The path to the Cardano node Unix socket.</param>
/// <param name="networkMagic">The network magic number.</param>
public class Ouroboros(string socketPath, ulong networkMagic = 2) : ICardanoDataProvider
{
    private readonly string _socketPath = socketPath ?? throw new ArgumentNullException(nameof(socketPath));
    private readonly ulong _networkMagic = networkMagic;

    /// <summary>
    /// Gets the network type based on network magic.
    /// </summary>
    public NetworkType NetworkType =>
        _networkMagic switch
        {
            764824073 => NetworkType.Mainnet,
            1 => NetworkType.Preprod,
            _ => NetworkType.Preview
        };

    /// <summary>
    /// Gets the network magic for a given network type.
    /// </summary>
    /// <param name="networkType">The network type.</param>
    /// <returns>The network magic number.</returns>
    public static ulong GetNetworkMagic(NetworkType networkType)
    {
        return networkType switch
        {
            NetworkType.Mainnet => 764824073UL,
            NetworkType.Preprod => 1UL,
            NetworkType.Testnet => throw new NotImplementedException(),
            NetworkType.Preview => throw new NotImplementedException(),
            NetworkType.Unknown => throw new NotImplementedException(),
            _ => 2UL
        };
    }

    /// <summary>
    /// Retrieves protocol parameters via local state query.
    /// </summary>
    /// <returns>The current protocol parameters.</returns>
    public async Task<ProtocolParams> GetParametersAsync()
    {
        NodeClient client = await NodeClient.ConnectAsync(_socketPath).ConfigureAwait(false);
        await client.StartAsync(_networkMagic).ConfigureAwait(false);

        CurrentProtocolParamsResponse currentProtocolParams = await client.LocalStateQuery.GetCurrentProtocolParamsAsync().ConfigureAwait(false);

        return currentProtocolParams.ProtocolParams;
    }

    /// <summary>
    /// Retrieves UTxOs for the given addresses via local state query.
    /// </summary>
    /// <param name="address">The list of Bech32 addresses.</param>
    /// <returns>A list of resolved inputs.</returns>
    public async Task<List<ResolvedInput>> GetUtxosAsync(List<string> address)
    {
        ArgumentNullException.ThrowIfNull(address);

        NodeClient client = await NodeClient.ConnectAsync(_socketPath).ConfigureAwait(false);
        await client.StartAsync(_networkMagic).ConfigureAwait(false);

        UtxoByAddressResponse utxos = await client.LocalStateQuery.GetUtxosByAddressAsync([.. address.Select(x => Address.FromBech32(x).ToBytes())]).ConfigureAwait(false);

        List<ResolvedInput> resolvedInputs = [];
        foreach ((CborTransactionInput? key, TransactionOutput? value) in utxos.Utxos)
        {
            ReadOnlyMemory<byte> txHash = key.TransactionId;
            ulong index = key.Index;

            ReadOnlyMemory<byte>? scriptRefBytes = value.ScriptRef();
            TransactionOutput output = new PostAlonzoTransactionOutput(
                new Cbor.Types.Cardano.Core.Common.Address(value.Address()),
                value.Amount(),
                value.DatumOption(),
                scriptRefBytes is not null ? new CborEncodedValue(scriptRefBytes.Value) : null
            );

            resolvedInputs.Add(new ResolvedInput(new CborTransactionInput(txHash, index), output));

        }
        return resolvedInputs;
    }

    /// <summary>
    /// Submits a signed transaction via the local tx submission mini-protocol.
    /// </summary>
    /// <param name="tx">The signed transaction.</param>
    /// <returns>The transaction hash.</returns>
    public async Task<string> SubmitTransactionAsync(Transaction tx)
    {
        ArgumentNullException.ThrowIfNull(tx);

        NodeClient client = await NodeClient.ConnectAsync(_socketPath).ConfigureAwait(false);
        await client.StartAsync(_networkMagic).ConfigureAwait(false);

        string txHex = Convert.ToHexString(CborSerializer.Serialize(tx));
        PostMaryTransaction postMaryTx = (PostMaryTransaction)tx;
        byte[] txBody = CborSerializer.Serialize(postMaryTx.TransactionBody);

        EraTx eraTx = new(6, new CborEncodedValue(Convert.FromHexString(txHex)));
        LocalTxSubmissionMessage result = await client.LocalTxSubmit.SubmitTxAsync(new SubmitTx(0, eraTx), CancellationToken.None).ConfigureAwait(false);

        string txHash = result switch
        {
            AcceptTx => Convert.ToHexString(HashUtil.Blake2b256(txBody)).ToUpperInvariant(),
            _ => throw new InvalidOperationException("Transaction submission failed")
        };

        return txHash;
    }

    /// <summary>
    /// Transaction metadata retrieval is not supported via Ouroboros protocol.
    /// </summary>
    /// <param name="txHash">The transaction hash.</param>
    /// <returns>Always throws NotImplementedException.</returns>
    public Task<Metadata?> GetTransactionMetadataAsync(string txHash)
    {
        throw new NotImplementedException("Transaction metadata retrieval is not supported via Ouroboros protocol");
    }
}
