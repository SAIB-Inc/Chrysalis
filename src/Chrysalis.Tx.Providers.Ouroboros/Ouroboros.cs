using Chrysalis.Codec.Types.Cardano.Core.Protocol;
using Chrysalis.Network.Cbor.Handshake;
using Chrysalis.Network.Cbor.LocalStateQuery;
using Chrysalis.Network.MiniProtocols.Extensions;
using Chrysalis.Network.Multiplexer;
using Chrysalis.Tx.Models;
using Chrysalis.Wallet.Models.Addresses;
using Chrysalis.Codec.Types.Cardano.Core.Transaction;
using Chrysalis.Codec.Extensions.Cardano.Core.Transaction;
using Chrysalis.Codec.Types;
using Chrysalis.Network.Cbor.LocalTxSubmit;
using Chrysalis.Codec.Serialization;
using Chrysalis.Wallet.Utils;
using Chrysalis.Tx.Models.Cbor;
using Chrysalis.Codec.Types.Cardano.Core;
using Chrysalis.Wallet.Models.Enums;

namespace Chrysalis.Tx.Providers;

/// <summary>
/// Ouroboros mini-protocol provider. Opens a connection per operation and supports
/// neither metadata nor output-reference lookup. Submission uses a fixed era tag.
/// </summary>
/// <param name="socketPath">The path to the Cardano node Unix socket.</param>
/// <param name="networkMagic">The network magic number.</param>
public class Ouroboros(string socketPath, ulong networkMagic = 2) : ICardanoDataProvider
{
    private readonly string _socketPath = socketPath ?? throw new ArgumentNullException(nameof(socketPath));
    private readonly ulong _networkMagic = networkMagic;
    private const string Provider = nameof(Ouroboros);

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
    public static ulong GetNetworkMagic(NetworkType networkType) => networkType switch
    {
        NetworkType.Mainnet => 764824073UL,
        NetworkType.Preprod => 1UL,
        NetworkType.Testnet => throw new NotImplementedException(),
        NetworkType.Preview => throw new NotImplementedException(),
        NetworkType.Unknown => throw new NotImplementedException(),
        _ => 2UL
    };

    /// <summary>
    /// Retrieves protocol parameters via local state query.
    /// </summary>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>The current protocol parameters.</returns>
    public Task<ProtocolParams> GetParametersAsync(CancellationToken cancellationToken = default) =>
        RunAsync(nameof(GetParametersAsync), async (client, ct) =>
        {
            CurrentProtocolParamsResponse answer = await client.LocalStateQuery.GetCurrentProtocolParamsAsync(ct).ConfigureAwait(false);
            return answer.ProtocolParams;
        }, cancellationToken);

    /// <summary>
    /// Retrieves UTxOs for the given addresses via local state query.
    /// </summary>
    /// <param name="address">The list of Bech32 addresses.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>A list of resolved inputs.</returns>
    public Task<List<ResolvedInput>> GetUtxosAsync(List<string> address, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(address);
        List<ReadOnlyMemory<byte>> addresses = [];
        foreach (string text in address)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(text, nameof(address));
            try { addresses.Add(Address.FromBech32(text).ToBytes()); }
            catch (Exception error) when (error is FormatException or IndexOutOfRangeException or ArgumentException)
            {
                throw new ArgumentException("Invalid Bech32 address.", nameof(address));
            }
        }
        cancellationToken.ThrowIfCancellationRequested();
        if (addresses.Count == 0) { return Task.FromResult<List<ResolvedInput>>([]); }
        return RunAsync(nameof(GetUtxosAsync), async (client, ct) =>
        {
            UtxoByAddressResponse answer = await client.LocalStateQuery.GetUtxosByAddressAsync(addresses, ct).ConfigureAwait(false);
            return MapOutputs(answer);
        }, cancellationToken);
    }

    private static List<ResolvedInput> MapOutputs(UtxoByAddressResponse utxos)
    {
        ResolvedInputSet resolvedInputs = new(Provider, nameof(GetUtxosAsync));
        foreach ((TransactionInput key, ITransactionOutput value) in utxos.Utxos)
        {
            ReadOnlyMemory<byte> txHash = key.TransactionId;
            ulong index = key.Index;

            ReadOnlyMemory<byte>? scriptRefBytes = value.ScriptRef();
            ITransactionOutput output = PostAlonzoTransactionOutput.Create(
                new Codec.Types.Cardano.Core.Common.Address(value.Address()),
                value.Amount(),
                value.DatumOption(),
                scriptRefBytes is not null ? CborEncodedValue.Read(scriptRefBytes.Value) : null
            );

            resolvedInputs.Add("addresses", new ResolvedInput(TransactionInput.Create(txHash, index), output));

        }
        return resolvedInputs.ToList();
    }

    /// <summary>
    /// Retrieves a specific UTxO by its output reference.
    /// Not supported by Ouroboros — provide the UTxO directly via InputOptions.Utxo.
    /// </summary>
    /// <param name="txHash">The transaction hash.</param>
    /// <param name="index">The output index.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>Always throws.</returns>
    public Task<ResolvedInput?> GetUtxoByOutRefAsync(string txHash, ulong index, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(txHash);
        cancellationToken.ThrowIfCancellationRequested();
        throw new NotSupportedException("Ouroboros provider does not support UTxO lookup by output reference. Provide the UTxO directly via InputOptions.Utxo.");
    }

    /// <summary>
    /// Submits a signed transaction via the local tx submission mini-protocol.
    /// </summary>
    /// <param name="tx">The signed transaction.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>The transaction hash.</returns>
    public Task<string> SubmitTransactionAsync(ITransaction tx, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(tx);
        if (tx is not PostMaryTransaction postMaryTx) { throw new ArgumentException("Expected a post-Mary transaction.", nameof(tx)); }
        cancellationToken.ThrowIfCancellationRequested();
        byte[] bytes = CborSerializer.Serialize(tx);
        byte[] body = CborSerializer.Serialize(postMaryTx.Body);
        const string operation = nameof(SubmitTransactionAsync);
        return RunAsync(operation, async (client, ct) =>
        {
            LocalTxSubmissionMessage result;
            try
            {
                result = await client.LocalTxSubmit.SubmitTxAsync(new SubmitTx(0, new EraTx(6, new CborEncodedValue(bytes))), ct).ConfigureAwait(false);
            }
            catch (Exception error) when (error is not OutOfMemoryException)
            {
                Exception translated = ProviderHttp.TransportFailure(Provider, operation, error, ct);
                throw translated is ProviderException { IsTransient: true }
                    ? new ProviderException($"{translated.Message}; the transaction may have been accepted", isTransient: true)
                    : translated;
            }
            return result is AcceptTx ? Convert.ToHexString(HashUtil.Blake2b256(body))
                : throw new ProviderException($"{Provider} {operation}: transaction rejected", isTransient: false);
        }, cancellationToken);
    }

    /// <summary>Transaction metadata retrieval is not supported via Ouroboros.</summary>
    /// <param name="txHash">The transaction hash.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>Always throws.</returns>
    public Task<Metadata?> GetTransactionMetadataAsync(string txHash, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(txHash);
        cancellationToken.ThrowIfCancellationRequested();
        throw new NotSupportedException("Transaction metadata retrieval is not supported via Ouroboros protocol.");
    }

    private async Task<T> RunAsync<T>(string operation, Func<NodeClient, CancellationToken, Task<T>> run, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            using NodeClient client = await NodeClient.ConnectAsync(_socketPath, cancellationToken).ConfigureAwait(false);
            await client.StartAsync(VersionTables.N2cV10AndAbove(_networkMagic), cancellationToken).ConfigureAwait(false);
            T result = await run(client, cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            return result;
        }
        catch (Exception error) when (error is not (ProviderException or OutOfMemoryException))
        {
            throw ProviderHttp.TransportFailure(Provider, operation, error, cancellationToken);
        }
    }
}
