using System.Buffers;
using System.IO.Pipelines;
using System.Threading.Channels;
using Chrysalis.Network.Core;

namespace Chrysalis.Network.Multiplexer;

/// <summary>
/// Provides bidirectional multiplexing capabilities over a single bearer.
/// </summary>
/// <remarks>
/// The Plexer coordinates a Demuxer and Muxer to handle inbound and outbound
/// communication through protocol-specific channels. It enables multiple
/// logical protocols to share a single physical connection.
/// </remarks>
/// <remarks>
/// Initializes a new instance of the <see cref="Plexer"/> class.
/// </remarks>
/// <param name="bearer">The bearer providing the physical connection.</param>
/// <exception cref="ArgumentNullException">Thrown if bearer is null.</exception>
public sealed class Plexer(IBearer bearer) : IDisposable
{
    public readonly Demuxer _demuxer = new(bearer);
    public readonly Muxer _muxer = new(bearer, ProtocolMode.Initiator);
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
    /// <remarks>
    /// Functionally equivalent to SubscribeClient, but semantic difference
    /// indicates server-side protocol handling.
    /// </remarks>
    public AgentChannel SubscribeServer(ProtocolType protocol)
    {
        PipeWriter writer = _muxer.Writer;
        PipeReader reader = _demuxer.Subscribe(protocol);
        return new AgentChannel(protocol, writer, reader);
    }

    /// <summary>
    /// Starts the plexer, running both the demuxer and muxer.
    /// </summary>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>A task that completes when either the demuxer or muxer stops.</returns>
    /// <exception cref="InvalidOperationException">Thrown if the plexer is already running.</exception>
    public async Task RunAsync(CancellationToken cancellationToken)
    {
        await Task.WhenAny(
            _demuxer.RunAsync(cancellationToken),
            _muxer.RunAsync(cancellationToken)
        );

        throw new Exception("Something went wrong");
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