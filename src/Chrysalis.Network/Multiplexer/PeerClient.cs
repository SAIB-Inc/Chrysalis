using Chrysalis.Network.Cbor.Handshake;
using Chrysalis.Network.Core;
using Chrysalis.Network.MiniProtocols;

namespace Chrysalis.Network.Multiplexer;

/// <summary>
/// Provides a high-level N2N client for connecting to Cardano nodes over TCP.
/// </summary>
public sealed class PeerClient : IDisposable, IAsyncDisposable
{
    /// <summary>Gets the network magic number used during the handshake (e.g., 764824073 for mainnet).</summary>
    public ulong NetworkMagic { get; private set; } = 2;

    private readonly Plexer _plexer;
    private Task? _plexerTask;
    private Task? _keepAliveTask;
    private readonly CancellationTokenSource _keepAliveCts = new();
    private readonly CancellationToken _keepAliveToken;
    private int _started;
    private int _disposed;

    internal ReplyLimit Replies { get; }

    /// <summary>Gets the accepted node-to-node version, or null before successful negotiation.</summary>
    public N2NVersion? NegotiatedVersion { get; private set; }

    /// <summary>
    /// Gets a task that settles once when the connection ends: ran to completion on local
    /// <see cref="Dispose"/> or <see cref="DisposeAsync"/> (and after a failed <see cref="StartAsync"/>
    /// with no prior connection fault), or faulted with <see cref="ConnectionClosedException"/> on a peer close or fault.
    /// </summary>
    /// <remarks>The first end wins. Awaiting this task observes the connection's end without polling.</remarks>
    public Task Completion => _plexer.Completion;

    /// <summary>
    /// Gets the Handshake protocol handler.
    /// </summary>
    public Handshake Handshake { get; private set; }

    /// <summary>
    /// Gets the ChainSync protocol handler (N2N).
    /// </summary>
    public ChainSync ChainSync { get; private set; }

    /// <summary>
    /// Gets the KeepAlive protocol handler (N2N).
    /// </summary>
    public KeepAliveClient KeepAlive { get; private set; }

    /// <summary>
    /// Gets the BlockFetch protocol handler (N2N).
    /// </summary>
    public BlockFetch BlockFetch { get; private set; }

    private PeerClient(Plexer plexer)
    {
        _plexer = plexer ?? throw new ArgumentNullException(nameof(plexer));
        _keepAliveToken = _keepAliveCts.Token;
        AgentChannel handshake = _plexer.SubscribeClient(ProtocolType.Handshake);
        Replies = new ReplyLimit(handshake);
        Handshake = new(handshake);
        ChainSync = new(_plexer.SubscribeClient(ProtocolType.NodeChainSync), ProtocolType.NodeChainSync);
        KeepAlive = new(_plexer.SubscribeClient(ProtocolType.KeepAlive));
        BlockFetch = new(_plexer.SubscribeClient(ProtocolType.BlockFetch));
    }

    /// <summary>
    /// Creates and connects a new PeerClient instance to a Cardano node over a Unix domain socket.
    /// </summary>
    /// <param name="socketPath">The path to the node's Unix domain socket.</param>
    /// <param name="cancellationToken">A token to cancel the connection operation.</param>
    /// <returns>A connected PeerClient instance.</returns>
    public static async Task<PeerClient> ConnectAsync(string socketPath, CancellationToken cancellationToken = default)
    {
        UnixBearer? unixBearer = null;
        try
        {
            unixBearer = await UnixBearer.CreateAsync(socketPath, cancellationToken).ConfigureAwait(false);
            PeerClient client = CreateFromBearer(unixBearer);
            unixBearer = null; // Ownership transferred
            return client;
        }
        finally
        {
            unixBearer?.Dispose();
        }
    }

    /// <summary>
    /// Creates and connects a new PeerClient instance to a Cardano node over TCP.
    /// </summary>
    /// <param name="host">The host to connect to.</param>
    /// <param name="port">The port to connect to.</param>
    /// <param name="cancellationToken">A token to cancel the connection operation.</param>
    /// <returns>A connected PeerClient instance.</returns>
    public static async Task<PeerClient> ConnectAsync(string host, int port, CancellationToken cancellationToken = default)
    {
        TcpBearer? tcpBearer = null;
        try
        {
            tcpBearer = await TcpBearer.CreateAsync(host, port, cancellationToken).ConfigureAwait(false);
            PeerClient client = CreateFromBearer(tcpBearer);
            tcpBearer = null; // Ownership transferred
            return client;
        }
        finally
        {
            tcpBearer?.Dispose();
        }
    }

    internal static PeerClient CreateFromBearer(IBearer bearer)
    {
        Plexer plexer = new(bearer);
        try
        {
            PeerClient client = new(plexer);
            return client;
        }
        catch
        {
            plexer.Dispose();
            throw;
        }
    }

    /// <summary>
    /// Starts the client: the handshake, then the keep-alive loop.
    /// </summary>
    /// <param name="networkMagic">Network magic for handshake.</param>
    /// <param name="keepAliveInterval">Interval between keepalive roundtrips.</param>
    /// <param name="cancellationToken">Cancels startup; it does not change the lifetime of an accepted connection.</param>
    /// <remarks>
    /// The peer has 10 seconds to answer the handshake and 60 seconds to answer each keep-alive; a limit that passes ends the
    /// connection, and its calls throw <see cref="ConnectionClosedException"/> with a <see cref="ReplyTimeoutException"/> inside.
    /// A keep-alive protocol error also ends the connection. Pending and later mini-protocol calls on an ended connection throw
    /// <see cref="ConnectionClosedException"/>. A start that fails, or is cancelled after it has begun, disposes the connection.
    /// </remarks>
    /// <exception cref="ObjectDisposedException">The client has been disposed.</exception>
    /// <exception cref="InvalidOperationException">The client was already started.</exception>
    public async Task StartAsync(ulong networkMagic = 2, TimeSpan? keepAliveInterval = null, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        cancellationToken.ThrowIfCancellationRequested();
        if (Interlocked.Exchange(ref _started, 1) != 0)
        {
            throw new InvalidOperationException("The client has already started its handshake.");
        }
        _plexerTask = _plexer.RunAsync(CancellationToken.None);

        NetworkMagic = networkMagic;

        ProposeVersions proposeVersion = HandshakeMessages.ProposeVersions(VersionTables.N2nV11AndAbove(networkMagic));
        try
        {
            using ReplyLimit.Wait wait = Replies.Start(ProtocolType.Handshake, ReplyLimit.Handshake);
            N2NAcceptVersion accepted = await Handshake.SendAsync<N2NAcceptVersion>(proposeVersion, cancellationToken).ConfigureAwait(false);
            NegotiatedVersion = accepted.Version;
        }
        catch
        {
            _plexer.Dispose();
            throw;
        }

        TimeSpan interval = keepAliveInterval ?? TimeSpan.FromSeconds(20);
        _keepAliveTask = RunKeepAliveLoop(interval, _keepAliveToken);
    }

    /// <summary>
    /// Checks if the plexer is running and its connection has not ended.
    /// </summary>
    /// <returns><see langword="true"/> once started and while <see cref="Completion"/> is pending; otherwise <see langword="false"/>.</returns>
    public bool IsPlexerHealthy() => _plexer.IsPlexerHealthy();

    /// <summary>
    /// Gets the exception that ended the connection, if any.
    /// </summary>
    /// <returns>
    /// The first fault cause, the <see cref="ConnectionClosedException"/> itself on a clean peer close,
    /// or <see langword="null"/> before an end or after a local disposal.
    /// </returns>
    public Exception? GetPlexerException() => _plexer.GetPlexerException();

    /// <summary>
    /// Checks if the keepalive loop is running on a live connection.
    /// </summary>
    /// <returns>
    /// <see langword="true"/> while the keep-alive loop has not completed and <see cref="Completion"/> is still pending;
    /// otherwise <see langword="false"/> (an idle loop exits quietly at its next tick or on disposal).
    /// </returns>
    public bool IsKeepAliveHealthy() => _keepAliveTask is { IsCompleted: false } && !Completion.IsCompleted;

    /// <summary>
    /// Gets the exception that caused the keepalive loop to fail, if any.
    /// </summary>
    /// <returns>
    /// The underlying <see cref="ReplyTimeoutException"/> on a keep-alive reply timeout, the protocol error on a
    /// keep-alive mismatch or decode failure, or <see langword="null"/> while running or after an external or local close.
    /// </returns>
    public Exception? GetKeepAliveException() => Plexer.ExtractException(_keepAliveTask);

    private async Task RunKeepAliveLoop(TimeSpan interval, CancellationToken cancellationToken)
    {
        using PeriodicTimer timer = new(interval, Replies.Clock);
        try
        {
            while (await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false))
            {
                // A peer that stops answering keep-alives is silent: when the limit passes, the connection ends for every caller.
                using ReplyLimit.Wait wait = Replies.Start(ProtocolType.KeepAlive, ReplyLimit.KeepAlive);
                await KeepAlive.KeepAliveRoundtripAsync(cancellationToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            // Expected during shutdown.
        }
        catch (ConnectionClosedException error) when (error.InnerException is not ReplyTimeoutException { Protocol: ProtocolType.KeepAlive })
        {
            // The connection already ended for another reason; stop the loop quietly.
        }
        catch (ConnectionClosedException)
        {
            // Keep-alive's own reply limit ended the connection; stay faulted so GetKeepAliveException reports the ReplyTimeoutException.
            throw;
        }
        catch (Exception error)
        {
            // A keep-alive protocol error ends the connection (first cause wins) and keeps the loop faulted with that error.
            _plexer.End(error);
            throw;
        }
    }

    /// <summary>
    /// Disposes the client and releases all resources.
    /// </summary>
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 0)
        {
            _keepAliveCts.Cancel();
            _keepAliveCts.Dispose();
        }
        _plexer.Dispose();
    }

    /// <summary>
    /// Disposes the client and completes once its read loop and keep-alive loop have stopped and its socket is closed.
    /// It may be called more than once, and after <see cref="Dispose"/>.
    /// </summary>
    public ValueTask DisposeAsync()
    {
        Dispose();
        return new ValueTask(StopAsync());
    }

    private async Task StopAsync()
    {
        await _plexer.CloseAsync().ConfigureAwait(false);
        if (_plexerTask is Task run)
        {
            await run.ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
        }
        if (_keepAliveTask is Task keepAlive)
        {
            await keepAlive.ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
        }
    }
}
