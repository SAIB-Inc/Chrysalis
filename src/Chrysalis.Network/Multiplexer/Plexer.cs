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
    private int _started;

    /// <summary>
    /// Gets a task that settles once when the connection ends: ran to completion on local
    /// <see cref="Dispose"/> or <see cref="RunAsync"/> cancellation, or faulted with
    /// <see cref="ConnectionClosedException"/> on a peer close or any connection fault.
    /// </summary>
    /// <remarks>
    /// The first end wins; later ends (including a subsequent <see cref="Dispose"/>) do not alter
    /// <see cref="Completion"/> or its cause. Pending and later channel calls throw
    /// <see cref="ConnectionClosedException"/> whose <see cref="Exception.InnerException"/> is that
    /// first cause, or <see langword="null"/> on a clean peer close (after queued messages drain).
    /// </remarks>
    public Task Completion => _connection.Completion;

    internal bool IsPlexerHealthy() => Volatile.Read(ref _started) != 0 && !Completion.IsCompleted;

    internal Exception? GetPlexerException() => ExtractException(Completion);

    internal static Exception? ExtractException(Task? task)
    {
        if (task?.Exception is AggregateException aggregate && aggregate.InnerExceptions.Count > 0)
        {
            Exception first = aggregate.InnerExceptions[0];
            return first is ConnectionClosedException closed ? closed.InnerException ?? closed : first;
        }
        return null;
    }

    internal void End(Exception cause) => _connection.TryEnd(cause);

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
    /// Starts reading from the bearer and runs until the connection ends.
    /// </summary>
    /// <param name="cancellationToken">A token to cancel the read loop and end the connection locally.</param>
    /// <remarks>
    /// Returns normally after <see cref="Dispose"/>, throws <see cref="OperationCanceledException"/>
    /// when <paramref name="cancellationToken"/> ends the connection, and throws
    /// <see cref="ConnectionClosedException"/> on any peer close or connection fault.
    /// </remarks>
    /// <exception cref="InvalidOperationException">Thrown if <see cref="RunAsync"/> was already called.</exception>
    /// <exception cref="OperationCanceledException">Thrown when <paramref name="cancellationToken"/> ends the connection.</exception>
    /// <exception cref="ConnectionClosedException">Thrown when the peer closes or any connection fault ends the connection.</exception>
    public async Task RunAsync(CancellationToken cancellationToken)
    {
        Task demuxRun = _demuxer.RunAsync(cancellationToken);
        Volatile.Write(ref _started, 1);
        try
        {
            await demuxRun.ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested && _connection.Cause is OperationCanceledException)
        {
            await _connection.Completion.ConfigureAwait(false);
            throw;
        }
        catch (Exception)
        {
            // Connection.End already recorded the first cause on Completion; await it below
            // so RunAsync throws the connection's ConnectionClosedException rather than the raw cause.
        }
        await _connection.Completion.ConfigureAwait(false);
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
