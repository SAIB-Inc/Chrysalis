using System.IO.Pipelines;
using Chrysalis.Network.Core;

namespace Chrysalis.Network.Multiplexer;

/// <summary>
/// Provides bidirectional multiplexing capabilities over a single bearer.
/// </summary>
/// <remarks>
/// Initializes a new instance of the Plexer class.
/// </remarks>
/// <param name="bearer">The bearer providing the physical connection.</param>
public sealed class Plexer(IBearer bearer) : IDisposable
{
    private readonly Demuxer _demuxer = new(bearer);
    private readonly Muxer _muxer = new(bearer, ProtocolMode.Initiator);
    private bool _isDisposed;

    /// <summary>
    /// Creates a channel for client-side protocol communication.
    /// </summary>
    /// <param name="protocol">The protocol type to subscribe to.</param>
    /// <returns>A bidirectional agent channel for the specified protocol.</returns>
    public AgentChannel SubscribeClient(ProtocolType protocol)
    {
        PipeWriter writer = _muxer.Writer;
        PipeReader reader = _demuxer.Subscribe(protocol);
        return new AgentChannel(protocol, writer, reader);
    }

    /// <summary>
    /// Creates a channel for server-side protocol communication.
    /// </summary>
    /// <param name="protocol">The protocol type to subscribe to.</param>
    /// <returns>A bidirectional agent channel for the specified protocol.</returns>
    public AgentChannel SubscribeServer(ProtocolType protocol) => SubscribeClient(protocol);
    /// <summary>
    /// Starts the plexer, running both the demuxer and muxer.
    /// </summary>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>A task that completes when the plexer stops.</returns>
    /// <exception cref="InvalidOperationException">Thrown if either demuxer or muxer completes unexpectedly.</exception>
    public async Task RunAsync(CancellationToken cancellationToken)
    {
        Task demuxerTask = _demuxer.RunAsync(cancellationToken);
        Task muxerTask = _muxer.RunAsync(cancellationToken);

        Task completedTask = await Task.WhenAny(demuxerTask, muxerTask);

        // Check which task completed and propagate its exception (if faulted)
        if (completedTask == demuxerTask)
        {
            await demuxerTask; // Re-throws exception if faulted
            throw new InvalidOperationException("Demuxer completed unexpectedly without error");
        }
        else
        {
            await muxerTask; // Re-throws exception if faulted
            throw new InvalidOperationException("Muxer completed unexpectedly without error");
        }
    }

    /// <summary>
    /// Disposes the resources used by the plexer.
    /// </summary>
    public void Dispose()
    {
        if (_isDisposed)
            return;

        _muxer.Dispose();
        _demuxer.Dispose();

        _isDisposed = true;
        GC.SuppressFinalize(this);
    }
}