using Chrysalis.Network.Core;
using Chrysalis.Network.MiniProtocols;

namespace Chrysalis.Network.Multiplexer;

/// <summary>
/// Provides a high-level client for connecting to Cardano nodes.
/// </summary>
/// <remarks>
/// NodeClient encapsulates the multiplexing infrastructure and provides
/// convenient access to the Cardano mini-protocols. The typical usage is
/// to create an instance via <see cref="ConnectAsync"/> and then call <see cref="Start"/>.
/// </remarks>
public class NodeClient : IDisposable
{
    private readonly Plexer _plexer;
    private bool _isDisposed;

    #region MiniProtocols
    /// <summary>
    /// Gets the Handshake protocol handler, available after <see cref="Start"/> is called.
    /// </summary>
    public Handshake? Handshake { get; private set; }

    /// <summary>
    /// Gets the LocalStateQuery protocol handler, available after <see cref="Start"/> is called.
    /// </summary>
    public LocalStateQuery? LocalStateQuery { get; private set; }

    /// <summary>
    /// Gets the ChainSync protocol handler, available after <see cref="Start"/> is called.
    /// </summary>
    public ChainSync? ChainSync { get; private set; }
    #endregion

    private NodeClient(Plexer plexer)
    {
        _plexer = plexer ?? throw new ArgumentNullException(nameof(plexer));
    }

    /// <summary>
    /// Creates and connects a new NodeClient instance to a Cardano node.
    /// </summary>
    /// <param name="socketPath">The path to the node's Unix domain socket.</param>
    /// <returns>A connected NodeClient instance.</returns>
    /// <exception cref="IOException">Thrown if the connection to the socket fails.</exception>
    public static async Task<NodeClient> ConnectAsync(string socketPath)
    {
        UnixBearer unixBearer = await UnixBearer.CreateAsync(socketPath);
        Plexer plexer = new(unixBearer);
        return new(plexer);
    }

    /// <summary>
    /// Starts the client and initializes protocol handlers.
    /// </summary>
    /// <remarks>
    /// This method starts the multiplexer's processing loop and creates
    /// protocol handlers for Handshake, ChainSync, and LocalStateQuery.
    /// Must be called after connection and before using any protocol handler.
    /// </remarks>
    public void Start()
    {
        _ = _plexer.RunAsync(CancellationToken.None);
        Handshake = new(_plexer.SubscribeClient(ProtocolType.Handshake));
        ChainSync = new(_plexer.SubscribeClient(ProtocolType.ClientChainSync));
        LocalStateQuery = new(_plexer.SubscribeClient(ProtocolType.LocalStateQuery));
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