using Chrysalis.Network.Core;

namespace Chrysalis.Network.Multiplexer;

/// <summary>
/// Provides bidirectional multiplexing capabilities over a single bearer.
/// </summary>
public sealed class Plexer : IDisposable
{
    private readonly IBearer _bearer;
    private readonly Connection _connection;
    private readonly Demuxer _demuxer;

    /// <summary>
    /// Initializes a new instance of the <see cref="Plexer"/> class.
    /// </summary>
    /// <param name="bearer">The bearer for network communication.</param>
    public Plexer(IBearer bearer)
    {
        _bearer = bearer ?? throw new ArgumentNullException(nameof(bearer));
        _connection = new Connection(bearer);
        _demuxer = new Demuxer(bearer, _connection);
    }

    /// <summary>
    /// Creates a channel for client-side protocol communication.
    /// </summary>
    /// <param name="protocol">The protocol type to subscribe to.</param>
    /// <remarks>A full 64-item protocol queue stalls all protocols until an item is consumed.</remarks>
    /// <returns>A bidirectional agent channel for the specified protocol.</returns>
    public AgentChannel SubscribeClient(ProtocolType protocol) => Subscribe(protocol, ProtocolMode.Initiator);

    /// <summary>
    /// Creates a channel for server-side protocol communication.
    /// </summary>
    /// <param name="protocol">The protocol type to subscribe to.</param>
    /// <remarks>A full 64-item protocol queue stalls all protocols until an item is consumed.</remarks>
    /// <returns>A bidirectional agent channel for the specified protocol.</returns>
    public AgentChannel SubscribeServer(ProtocolType protocol) => Subscribe(protocol, ProtocolMode.Responder);

    private AgentChannel Subscribe(ProtocolType protocol, ProtocolMode mode) =>
        new(protocol, _bearer.Writer, _demuxer.Subscribe(protocol), _connection, mode);

    /// <summary>
    /// Starts reading from the bearer.
    /// </summary>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    public async Task RunAsync(CancellationToken cancellationToken)
    {
        await _demuxer.RunAsync(cancellationToken).ConfigureAwait(false);
        throw new InvalidOperationException("Demuxer completed unexpectedly without error");
    }

    /// <summary>
    /// Disposes the resources used by the plexer.
    /// </summary>
    public void Dispose()
    {
        _demuxer.Dispose();
        _connection.DisposeBearer();
    }

    /// <summary>Disposes the plexer and completes when its bearer has been disposed.</summary>
    internal Task CloseAsync()
    {
        Dispose();
        return _connection.BearerDisposed;
    }
}
