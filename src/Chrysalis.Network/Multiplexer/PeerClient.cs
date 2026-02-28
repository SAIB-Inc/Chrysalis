using Chrysalis.Network.Cbor.Handshake;
using Chrysalis.Network.Core;
using Chrysalis.Network.MiniProtocols;

namespace Chrysalis.Network.Multiplexer;

/// <summary>
/// Provides a high-level N2N client for connecting to Cardano nodes over TCP.
/// </summary>
public sealed class PeerClient : IDisposable
{
    public ulong NetworkMagic { get; private set; } = 2;

    private readonly Plexer _plexer;
    private Task? _plexerTask;
    private Task? _keepAliveTask;
    private CancellationTokenSource? _keepAliveCts;

    /// <summary>
    /// Gets the Handshake protocol handler.
    /// </summary>
    public Handshake Handshake { get; private set; } = default!;

    /// <summary>
    /// Gets the ChainSync protocol handler (N2N).
    /// </summary>
    public ChainSync ChainSync { get; private set; } = default!;

    /// <summary>
    /// Gets the KeepAlive protocol handler (N2N).
    /// </summary>
    public KeepAliveClient KeepAlive { get; private set; } = default!;

    /// <summary>
    /// Gets the BlockFetch protocol handler (N2N).
    /// </summary>
    public BlockFetch BlockFetch { get; private set; } = default!;

    private PeerClient(Plexer plexer)
    {
        _plexer = plexer ?? throw new ArgumentNullException(nameof(plexer));
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

    private static PeerClient CreateFromBearer(IBearer bearer)
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
    /// Starts the client and initializes protocol handlers.
    /// </summary>
    /// <param name="networkMagic">Network magic for handshake.</param>
    /// <param name="keepAliveInterval">Interval between keepalive roundtrips.</param>
    public async Task StartAsync(ulong networkMagic = 2, TimeSpan? keepAliveInterval = null)
    {
        _plexerTask = _plexer.RunAsync(CancellationToken.None);

        Handshake = new(_plexer.SubscribeClient(ProtocolType.Handshake));
        ChainSync = new(_plexer.SubscribeClient(ProtocolType.NodeChainSync), ProtocolType.NodeChainSync);
        KeepAlive = new(_plexer.SubscribeClient(ProtocolType.KeepAlive));
        BlockFetch = new(_plexer.SubscribeClient(ProtocolType.BlockFetch));

        NetworkMagic = networkMagic;

        ProposeVersions proposeVersion = HandshakeMessages.ProposeVersions(VersionTables.N2nV11AndAbove(networkMagic));
        HandshakeMessage handshakeResponse = await Handshake.SendAsync(proposeVersion, CancellationToken.None).ConfigureAwait(false);

        if (handshakeResponse is not AcceptVersion)
        {
            throw new InvalidOperationException("Handshake failed");
        }

        TimeSpan interval = keepAliveInterval ?? TimeSpan.FromSeconds(20);
        _keepAliveCts = new CancellationTokenSource();
        _keepAliveTask = RunKeepAliveLoop(interval, _keepAliveCts.Token);
    }

    /// <summary>
    /// Checks if the plexer (multiplexer/demultiplexer) is healthy and running.
    /// </summary>
    public bool IsPlexerHealthy()
    {
        return _plexerTask is { IsCompleted: false };
    }

    /// <summary>
    /// Gets the exception that caused the plexer to fail, if any.
    /// </summary>
    public Exception? GetPlexerException()
    {
        return _plexerTask?.Exception?.GetBaseException();
    }

    /// <summary>
    /// Checks if the keepalive loop is healthy and running.
    /// </summary>
    public bool IsKeepAliveHealthy()
    {
        return _keepAliveTask is { IsCompleted: false };
    }

    /// <summary>
    /// Gets the exception that caused the keepalive loop to fail, if any.
    /// </summary>
    public Exception? GetKeepAliveException()
    {
        return _keepAliveTask?.Exception?.GetBaseException();
    }

    private async Task RunKeepAliveLoop(TimeSpan interval, CancellationToken cancellationToken)
    {
        using PeriodicTimer timer = new(interval);
        try
        {
            while (await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false))
            {
                await KeepAlive.KeepAliveRoundtripAsync(cancellationToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            // Expected during shutdown.
        }
    }

    /// <summary>
    /// Disposes the client and releases all resources.
    /// </summary>
    public void Dispose()
    {
        _keepAliveCts?.Cancel();
        _keepAliveCts?.Dispose();
        _plexer.Dispose();
    }
}
