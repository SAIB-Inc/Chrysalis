using Chrysalis.Network.Cbor.Handshake;
using Chrysalis.Network.Core;
using Chrysalis.Network.MiniProtocols;

namespace Chrysalis.Network.Multiplexer;

/// <summary>
/// Provides a high-level client for connecting to Cardano nodes.
/// </summary>
public sealed class NodeClient : IDisposable
{
    public ulong NetworkMagic { get; set; } = 2;

    private readonly Plexer _plexer;
    private Task? _plexerTask;

    /// <summary>
    /// Gets the Handshake protocol handler.
    /// </summary>
    public Handshake Handshake { get; private set; } = default!;

    /// <summary>
    /// Gets the LocalStateQuery protocol handler.
    /// </summary>
    public LocalStateQuery LocalStateQuery { get; private set; } = default!;

    /// <summary>
    /// Gets the ChainSync protocol handler.
    /// </summary>
    public ChainSync ChainSync { get; private set; } = default!;

    /// <summary>
    /// Gets the LocalTxSubmit protocol handler.
    /// </summary>
    public LocalTxSubmit LocalTxSubmit { get; private set; } = default!;

    /// <summary>
    /// Gets the LocalTxMonitor protocol handler.
    /// </summary>
    public LocalTxMonitor LocalTxMonitor { get; private set; } = default!;

    /// <summary>
    /// Initializes a new instance of the NodeClient class.
    /// </summary>
    /// <param name="plexer">The multiplexer for managing protocol channels.</param>
    private NodeClient(Plexer plexer)
    {
        _plexer = plexer ?? throw new ArgumentNullException(nameof(plexer));
    }

    /// <summary>
    /// Creates and connects a new NodeClient instance to a Cardano node.
    /// </summary>
    /// <param name="socketPath">The path to the node's Unix domain socket.</param>
    /// <param name="cancellationToken">A token to cancel the connection operation.</param>
    /// <returns>A connected NodeClient instance.</returns>
    /// <exception cref="InvalidOperationException">Thrown if the connection fails.</exception>
    public static async Task<NodeClient> ConnectAsync(string socketPath, CancellationToken cancellationToken = default)
    {
        UnixBearer? unixBearer = null;
        try
        {
            unixBearer = await UnixBearer.CreateAsync(socketPath, cancellationToken).ConfigureAwait(false);
            NodeClient client = CreateFromBearer(unixBearer);
            unixBearer = null; // Ownership transferred
            return client;
        }
        finally
        {
            unixBearer?.Dispose();
        }
    }

    /// <summary>
    /// Creates and connects a new NodeClient instance to a Cardano node over TCP.
    /// </summary>
    /// <param name="host">The host to connect to.</param>
    /// <param name="port">The port to connect to.</param>
    /// <param name="cancellationToken">A token to cancel the connection operation.</param>
    /// <returns>A connected NodeClient instance.</returns>
    public static async Task<NodeClient> ConnectAsync(string host, int port, CancellationToken cancellationToken = default)
    {
        TcpBearer? tcpBearer = null;
        try
        {
            tcpBearer = await TcpBearer.CreateAsync(host, port, cancellationToken).ConfigureAwait(false);
            NodeClient client = CreateFromBearer(tcpBearer);
            tcpBearer = null; // Ownership transferred
            return client;
        }
        finally
        {
            tcpBearer?.Dispose();
        }
    }

    private static NodeClient CreateFromBearer(IBearer bearer)
    {
        Plexer plexer = new(bearer);
        try
        {
            NodeClient client = new(plexer);
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
    /// <param name="networkMagic">The network magic number for the handshake.</param>
    /// <exception cref="InvalidOperationException">Thrown if the client is already started.</exception>
    public async Task StartAsync(ulong networkMagic = 2)
    {
        // Store plexer task so we can observe failures
        _plexerTask = _plexer.RunAsync(CancellationToken.None);

        Handshake = new(_plexer.SubscribeClient(ProtocolType.Handshake));
        ChainSync = new(_plexer.SubscribeClient(ProtocolType.ClientChainSync));
        LocalTxSubmit = new(_plexer.SubscribeClient(ProtocolType.LocalTxSubmission));
        LocalStateQuery = new(_plexer.SubscribeClient(ProtocolType.LocalStateQuery));
        LocalTxMonitor = new(_plexer.SubscribeClient(ProtocolType.LocalTxMonitor));

        NetworkMagic = networkMagic;

        ProposeVersions proposeVersion = HandshakeMessages.ProposeVersions(VersionTables.N2cV10AndAbove(networkMagic));
        HandshakeMessage handshakeResponse = await Handshake.SendAsync(proposeVersion, CancellationToken.None).ConfigureAwait(false);

        if (handshakeResponse is not AcceptVersion)
        {
            throw new InvalidOperationException("Handshake failed");
        }
    }

    /// <summary>
    /// Checks if the plexer (multiplexer/demultiplexer) is healthy and running.
    /// </summary>
    /// <returns>True if the plexer is running normally, false if it has stopped or faulted.</returns>
    /// <remarks>
    /// Task.IsCompleted returns true when the task is in RanToCompletion, Faulted, or Canceled state,
    /// so this single check covers all failure scenarios.
    /// </remarks>
    public bool IsPlexerHealthy()
    {
        return _plexerTask is { IsCompleted: false };
    }

    /// <summary>
    /// Gets the exception that caused the plexer to fail, if any.
    /// </summary>
    /// <returns>The base exception from the plexer task, or null if no exception occurred.</returns>
    public Exception? GetPlexerException()
    {
        return _plexerTask?.Exception?.GetBaseException();
    }

    /// <summary>
    /// Disposes the client and releases all resources.
    /// </summary>
    public void Dispose()
    {
        _plexer.Dispose();
    }
}
