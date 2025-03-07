using Chrysalis.Network.Core;
using Chrysalis.Network.MiniProtocols;

namespace Chrysalis.Network.Multiplexer;

/// <summary>
/// Provides a high-level client for connecting to Cardano nodes.
/// </summary>
public class NodeClient : IDisposable
{
    private readonly Plexer _plexer;
    private bool _isDisposed;

    /// <summary>
    /// Gets the Handshake protocol handler.
    /// </summary>
    public Handshake? Handshake { get; private set; }

    /// <summary>
    /// Gets the LocalStateQuery protocol handler.
    /// </summary>
    public LocalStateQuery? LocalStateQuery { get; private set; }

    /// <summary>
    /// Gets the ChainSync protocol handler.
    /// </summary>
    public ChainSync? ChainSync { get; private set; }

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
        UnixBearer unixBearer = await UnixBearer.CreateAsync(socketPath, cancellationToken);
        Plexer plexer = new(unixBearer);
        return new(plexer);
    }

    /// <summary>
    /// Creates and connects a new NodeClient instance to a Cardano node over TCP.
    /// </summary>
    /// <param name="host">The host to connect to.</param>
    /// <param name="port">The port to connect to.</param>
    /// <param name="cancellationToken">A token to cancel the connection operation.</param>
    /// <returns>A connected NodeClient instance.</returns>
    public static async Task<NodeClient> ConnectTcpAsync(string host, int port, CancellationToken cancellationToken = default)
    {
        TcpBearer tcpBearer = await TcpBearer.CreateAsync(host, port, cancellationToken);
        Plexer plexer = new(tcpBearer);
        return new(plexer);
    }

    /// <summary>
    /// Starts the client and initializes protocol handlers.
    /// </summary>
    /// <param name="cancellationToken">A token to cancel the start operation.</param>
    /// <exception cref="InvalidOperationException">Thrown if the client is already started.</exception>
    public void Start()
    {
        _ = _plexer.RunAsync(CancellationToken.None);
        Handshake = new(_plexer.SubscribeClient(ProtocolType.Handshake));
        ChainSync = new(_plexer.SubscribeClient(ProtocolType.ClientChainSync));
    }

    /// <summary>
    /// Disposes the client and releases all resources.
    /// </summary>
    public void Dispose()
    {
        if (_isDisposed) return;

        _plexer.Dispose();
        _isDisposed = true;
        GC.SuppressFinalize(this);
    }
}