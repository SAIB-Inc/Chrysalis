using System.Globalization;
using System.Text.RegularExpressions;
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
using Chrysalis.Tx.Models.Cbor;
using Chrysalis.Codec.Types.Cardano.Core;
using Chrysalis.Wallet.Models.Enums;

namespace Chrysalis.Tx.Providers;

/// <summary>
/// Ouroboros mini-protocol provider over a node's local socket. Reuses one idle connection across sequential calls;
/// overlapping calls open their own. Dispose the provider when finished. Metadata lookup is not supported.
/// <see cref="NetworkType"/> is <see cref="NetworkType.Unknown"/> for a magic other than Mainnet's, Preprod's or Preview's.
/// </summary>
/// <param name="socketPath">The path to the Cardano node Unix socket.</param>
/// <param name="networkMagic">The network magic number.</param>
public sealed partial class Ouroboros(string socketPath, ulong networkMagic = 2) : ICardanoDataProvider, IDisposable, IAsyncDisposable
{
    private static readonly TimeSpan DefaultReplyTimeLimit = TimeSpan.FromSeconds(180);
    private static readonly TimeSpan MaxReplyTimeLimit = TimeSpan.FromMilliseconds(uint.MaxValue - 1);
    private readonly string _socketPath = socketPath ?? throw new ArgumentNullException(nameof(socketPath));
    private readonly ulong _networkMagic = networkMagic;
    private const string Provider = nameof(Ouroboros);
    private readonly Lock _gate = new();
    private readonly HashSet<NodeClient> _active = [];
    private NodeClient? _idle;
    private int _disposed;
    private long _replyTimeLimitTicks = DefaultReplyTimeLimit.Ticks;

    // Test seam for Chrysalis.Tx.Test to attach a fake clock before StartAsync without editing Chrysalis.Network.
    internal Action<NodeClient>? OnClientConnected { get; set; }

    private bool IsDisposed => Volatile.Read(ref _disposed) != 0;

    private static readonly Dictionary<NetworkType, ulong> Magics = new()
    {
        [NetworkType.Mainnet] = 764824073,
        [NetworkType.Preprod] = 1,
        [NetworkType.Preview] = 2,
    };

    /// <summary>Gets Mainnet, Preprod or Preview from the network magic, else Unknown.</summary>
    public NetworkType NetworkType =>
        Magics.FirstOrDefault(pair => pair.Value == _networkMagic, new(NetworkType.Unknown, 0)).Key;

    /// <summary>
    /// Gets or sets how long the node may take to send a reply it owes on connections opened or reused by this provider.
    /// The default is 180 seconds; <see cref="Timeout.InfiniteTimeSpan"/> turns the limit off.
    /// A new value applies to calls that start after it is set.
    /// </summary>
    /// <remarks>
    /// A large by-address UTxO query on mainnet can run longer than 180 seconds; raise this property or set it to
    /// <see cref="Timeout.InfiniteTimeSpan"/> before calling <see cref="GetUtxosAsync"/>.
    /// The handshake reply keeps <see cref="NodeClient"/>'s fixed 10-second limit.
    /// </remarks>
    /// <exception cref="ArgumentOutOfRangeException">
    /// The value is zero, negative and not <see cref="Timeout.InfiniteTimeSpan"/>, or longer than a timer accepts.
    /// </exception>
    public TimeSpan ReplyTimeLimit
    {
        get => new(Volatile.Read(ref _replyTimeLimitTicks));
        set
        {
            if (value != Timeout.InfiniteTimeSpan)
            {
                ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(value, TimeSpan.Zero);
                ArgumentOutOfRangeException.ThrowIfGreaterThan(value, MaxReplyTimeLimit);
            }
            Volatile.Write(ref _replyTimeLimitTicks, value.Ticks);
        }
    }

    /// <summary>Gets the network magic of Mainnet, Preprod or Preview.</summary>
    /// <param name="networkType">The network type.</param>
    /// <returns>The network magic number.</returns>
    /// <exception cref="ArgumentOutOfRangeException">For any other network.</exception>
    public static ulong GetNetworkMagic(NetworkType networkType) =>
        Magics.TryGetValue(networkType, out ulong magic) ? magic
            : throw new ArgumentOutOfRangeException(nameof(networkType), networkType,
                "Only Mainnet, Preprod and Preview have a fixed network magic; pass another network's magic to the constructor.");

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
    /// Reads the network's system start and era history from the node.
    /// </summary>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>The node's <see cref="SlotNetworkConfig"/>; it ends where the node's era history ends.</returns>
    public Task<SlotNetworkConfig> GetSlotNetworkConfigAsync(CancellationToken cancellationToken = default) =>
        RunAsync(nameof(GetSlotNetworkConfigAsync), async (client, ct) =>
        {
            SystemStartResponse start = await client.LocalStateQuery.GetSystemStartAsync(ct).ConfigureAwait(false);
            EraHistoryResponse history = await client.LocalStateQuery.GetEraHistoryAsync(ct).ConfigureAwait(false);
            return ProviderHttp.Map(history, answer => new SlotNetworkConfig(start.Utc,
                [.. answer.Summaries.Select(era => new SlotEra(era.Start.Slot, era.Parameters.SlotLengthMilliseconds))],
                answer.Summaries[^1].End?.Slot), Provider, nameof(GetSlotNetworkConfigAsync));
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
        ObjectDisposedException.ThrowIf(IsDisposed, this);
        if (addresses.Count == 0) { return Task.FromResult<List<ResolvedInput>>([]); }
        return RunAsync(nameof(GetUtxosAsync), async (client, ct) =>
        {
            UtxoByAddressResponse answer = await client.LocalStateQuery.GetUtxosByAddressAsync(addresses, ct).ConfigureAwait(false);
            return MapOutputs(answer, nameof(GetUtxosAsync));
        }, cancellationToken);
    }

    private static List<ResolvedInput> MapOutputs(UtxoByAddressResponse utxos, string operation)
    {
        ResolvedInputSet resolvedInputs = new(Provider, operation);
        foreach ((TransactionInput key, ITransactionOutput value) in utxos.Utxos)
        {
            resolvedInputs.Add(operation, ProviderHttp.Map((key, value), Checked, Provider, operation));
        }
        return resolvedInputs.ToList();

        // Outputs decode lazily, so each field is read once here: a malformed one is an unusable response. The output itself
        // is kept exactly as the node encoded it (list or map form), so datum hashes and scripts survive.
        static ResolvedInput Checked((TransactionInput Key, ITransactionOutput Value) pair)
        {
            _ = (pair.Value.Address(), pair.Value.Amount(), pair.Value.DatumHash(), pair.Value.ScriptRef());
            return new ResolvedInput(TransactionInput.Create(pair.Key.TransactionId, pair.Key.Index), pair.Value);
        }
    }

    /// <summary>
    /// Retrieves an unspent output by its output reference from the node's ledger at the volatile tip.
    /// </summary>
    /// <param name="txHash">The transaction hash, 64 hexadecimal digits.</param>
    /// <param name="index">The output index; the ledger's is 16 bits.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>The output as the node encodes it, or null when it is not unspent.</returns>
    public Task<ResolvedInput?> GetUtxoByOutRefAsync(string txHash, ulong index, CancellationToken cancellationToken = default)
    {
        byte[] hash = ProviderValues.TransactionHash(txHash);
        // The node cannot read a larger index (cardano-ledger BaseTypes.hs:925, Word16) and would drop the connection.
        ArgumentOutOfRangeException.ThrowIfGreaterThan(index, ushort.MaxValue);
        cancellationToken.ThrowIfCancellationRequested();
        const string operation = nameof(GetUtxoByOutRefAsync);
        return RunAsync(operation, async (client, ct) =>
        {
            UtxoByAddressResponse answer = await client.LocalStateQuery.GetUtxosByTxInAsync([TransactionInput.Create(hash, index)], ct).ConfigureAwait(false);
            return MapOutputs(answer, operation) switch
            {
                [] => null,
                [ResolvedInput one] when one.Outref.Index == index && one.Outref.TransactionId.Span.SequenceEqual(hash) => one,
                _ => throw ProviderHttp.Unusable(Provider, operation, "wrong output reference"),
            };
        }, cancellationToken);
    }

    /// <summary>
    /// Submits a signed transaction via the local tx submission mini-protocol, tagged with the era of its encoding.
    /// </summary>
    /// <param name="tx">The signed transaction, with an Alonzo, Babbage or Conway body.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>The transaction id in uppercase hex. A rejection's Detail holds the node's reason.</returns>
    public Task<string> SubmitTransactionAsync(ITransaction tx, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(tx);
        if (tx is not PostMaryTransaction postMaryTx) { throw new ArgumentException("Expected a post-Mary transaction.", nameof(tx)); }
        // The body decodes on each read (CborSerializerCodeGen.Emitter.cs:228-231), so read it once.
        ITransactionBody body = postMaryTx.Body;
        // The era of the transaction's encoding, not the ledger's (B5); the node re-reads it in the ledger's era.
        Era era = body switch
        {
            ConwayTransactionBody => Era.Conway,
            BabbageTransactionBody => Era.Babbage,
            AlonzoTransactionBody => Era.Alonzo,
            _ => throw new ArgumentException("Expected an Alonzo, Babbage or Conway transaction body.", nameof(tx)),
        };
        cancellationToken.ThrowIfCancellationRequested();
        ObjectDisposedException.ThrowIf(IsDisposed, this);
        byte[] bytes = CborSerializer.Serialize(tx);
        string id = body.Hash();
        const string operation = nameof(SubmitTransactionAsync);
        return RunAsync(operation, async (client, ct) =>
        {
            LocalTxSubmissionMessage result;
            try
            {
                result = await client.LocalTxSubmit.SubmitTxAsync(new SubmitTx(0, new EraTx((int)era, new CborEncodedValue(bytes))), ct).ConfigureAwait(false);
            }
            catch (Exception error) when (error is not OutOfMemoryException)
            {
                ObjectDisposedException.ThrowIf(IsProviderDisposeError(error, ct), this);
                Exception translated = ProviderHttp.TransportFailure(Provider, operation, error, ct);
                throw translated is ProviderException lost ? ProviderHttp.MayHaveBeenAccepted(lost) : translated;
            }
            // The node's reason goes in Detail: long runs shortened (Shorten), masked and limited to 1,024 characters (ICardanoDataProvider.cs:17-19).
            return result switch
            {
                AcceptTx => id,
                RejectTx rejected => throw new ProviderException($"{Provider} {operation}: transaction rejected", isTransient: false,
                    ProviderHttp.Redact(Shorten(rejected.Describe()), [])),
                _ => throw ProviderHttp.MayHaveBeenAccepted(ProviderHttp.Unusable(Provider, operation, "unexpected reply")),
            };
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
        ObjectDisposedException.ThrowIf(IsDisposed, this);
        try
        {
            NodeClient client = await AcquireClientAsync(cancellationToken).ConfigureAwait(false);
            bool reusable = false;
            try
            {
                T result = await run(client, cancellationToken).ConfigureAwait(false);
                reusable = true;
                return result;
            }
            finally
            {
                await ReleaseClientAsync(client, reusable).ConfigureAwait(false);
            }
        }
        catch (Exception error) when (error is not (ProviderException or OutOfMemoryException))
        {
            ObjectDisposedException.ThrowIf(IsProviderDisposeError(error, cancellationToken), this);
            Exception failure = ProviderHttp.TransportFailure(Provider, operation, error, cancellationToken);
            // The state-query helpers' text says what the node answered (LocalStateQueryExtension.cs:189,196); it goes in Detail.
            throw error is InvalidOperationException and not ConnectionClosedException && failure is ProviderException known
                ? new ProviderException(known.Message, known.IsTransient, ProviderHttp.Redact(error.Message, []))
                : failure;
        }
    }

    private bool IsProviderDisposeError(Exception error, CancellationToken cancellationToken) =>
        IsDisposed && !cancellationToken.IsCancellationRequested
        && (error is ObjectDisposedException || error is ConnectionClosedException { InnerException: ObjectDisposedException });

    private async Task<NodeClient> AcquireClientAsync(CancellationToken cancellationToken)
    {
        NodeClient? stale = null;
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed != 0, this);
            if (_idle is NodeClient cached)
            {
                _idle = null;
                if (cached.IsPlexerHealthy())
                {
                    cached.ReplyTimeLimit = ReplyTimeLimit;
                    _ = _active.Add(cached);
                    return cached;
                }
                stale = cached;
            }
        }
        if (stale is not null)
        {
            await stale.DisposeAsync().ConfigureAwait(false);
            ObjectDisposedException.ThrowIf(IsDisposed, this);
        }

        NodeClient client = await NodeClient.ConnectAsync(_socketPath, cancellationToken).ConfigureAwait(false);
        bool disposed;
        lock (_gate)
        {
            disposed = _disposed != 0;
            if (!disposed) { _ = _active.Add(client); }
        }
        if (disposed)
        {
            await client.DisposeAsync().ConfigureAwait(false);
            ObjectDisposedException.ThrowIf(true, this);
        }

        try
        {
            OnClientConnected?.Invoke(client);
            client.ReplyTimeLimit = ReplyTimeLimit;
            await client.StartAsync(VersionTables.N2cV10AndAbove(_networkMagic), cancellationToken).ConfigureAwait(false);
            return client;
        }
        catch
        {
            await ReleaseClientAsync(client, reusable: false).ConfigureAwait(false);
            throw;
        }
    }

    private async ValueTask ReleaseClientAsync(NodeClient client, bool reusable)
    {
        NodeClient? toDispose = client;
        lock (_gate)
        {
            _ = _active.Remove(client);
            // _disposed == 0 is a defensive guard so a client is never parked during the race window after Dispose releases _gate.
            if (reusable && _disposed == 0 && _idle is null && client.IsPlexerHealthy())
            {
                _idle = client;
                toDispose = null;
            }
        }
        if (toDispose is not null) { await toDispose.DisposeAsync().ConfigureAwait(false); }
    }

    /// <summary>Disposes the provider and closes its idle and active node connections.</summary>
    public void Dispose()
    {
        NodeClient? idle;
        NodeClient[]? active;
        lock (_gate)
        {
            Volatile.Write(ref _disposed, 1);
            idle = _idle;
            _idle = null;
            active = DetachActiveLocked();
        }
        idle?.Dispose();
        if (active is not null)
        {
            foreach (NodeClient client in active) { client.Dispose(); }
        }
    }

    /// <summary>
    /// Disposes the provider; the first call completes once its idle and active node connections have stopped their read loops
    /// and closed their sockets. Later calls (including after <see cref="Dispose"/>) are idempotent no-ops.
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        NodeClient? idle;
        NodeClient[]? active;
        lock (_gate)
        {
            Volatile.Write(ref _disposed, 1);
            idle = _idle;
            _idle = null;
            active = DetachActiveLocked();
        }
        // Start every DisposeAsync synchronously before the first await so active clients close immediately without calling sync Dispose() (CA1849).
        Task? idleClose = idle?.DisposeAsync().AsTask();
        Task[]? activeCloses = active is null ? null : Array.ConvertAll(active, static client => client.DisposeAsync().AsTask());
        if (idleClose is not null) { await idleClose.ConfigureAwait(false); }
        if (activeCloses is not null)
        {
            foreach (Task close in activeCloses) { await close.ConfigureAwait(false); }
        }
    }

    private NodeClient[]? DetachActiveLocked()
    {
        if (_active.Count == 0) { return null; }
        NodeClient[] active = [.. _active];
        _active.Clear();
        return active;
    }

    // A base64 or hex run longer than 128 characters (script bytes, an encoded script context) keeps its first and last 8
    // characters and its length, so what follows it, such as a script failure's hash and evaluation error, fits in Detail.
    private static string Shorten(string text) => LongRun().Replace(text, run =>
        string.Create(CultureInfo.InvariantCulture, $"{run.ValueSpan[..8]}...({run.Length} characters)...{run.ValueSpan[^8..]}"));

    [GeneratedRegex("[A-Za-z0-9+/=]{129,}")]
    private static partial Regex LongRun();
}
