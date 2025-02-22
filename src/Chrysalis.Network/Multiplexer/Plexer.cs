using System;
using System.Threading.Tasks;
using Chrysalis.Network.Core;

namespace Chrysalis.Network.Multiplexer;

/// <summary>
/// Combines a Muxer and Demuxer to provide a full-duplex multiplexing/demultiplexing capability.
/// </summary>
/// <remarks>
/// Initializes a new instance of the <see cref="Plexer"/> class, creating a Muxer and Demuxer for the given bearer.
/// </remarks>
/// <param name="bearer">The bearer connection to use for multiplexing.</param>
public class Plexer(IBearer bearer) : IDisposable
{
    private readonly Demuxer _demuxer = new(bearer);
    private readonly Muxer _muxer = new(bearer, ProtocolMode.Initiator);
    private readonly CancellationTokenSource _plexerCts = new(); // CancellationTokenSource for Plexer lifetime

    /// <summary>
    /// Subscribes an agent channel for client-side protocol operation.
    /// </summary>
    /// <param name="protocol">The protocol type for the client channel.</param>
    /// <returns>A new AgentChannel configured for client-side protocol usage.</returns>
    public AgentChannel SubscribeClient(ProtocolType protocol)
    {
        var toPlexerSubject = _muxer.GetMessageSubject();
        var fromPlexerSubject = _demuxer.Subscribe(protocol);

        return new AgentChannel(protocol, toPlexerSubject, fromPlexerSubject);
    }

    /// <summary>
    /// Subscribes an agent channel for server-side protocol operation.
    /// </summary>
    /// <param name="protocol">The protocol type for the server channel.</param>
    /// <returns>A new AgentChannel configured for server-side protocol usage.</returns>
    public AgentChannel SubscribeServer(ProtocolType protocol)
    {
        var toPlexerSubject = _muxer.GetMessageSubject();
        var fromPlexerSubject = _demuxer.Subscribe(protocol);

        return new AgentChannel(protocol, toPlexerSubject, fromPlexerSubject);
    }

    /// <summary>
    /// Spawns the demuxer and muxer run loops, starting the plexer's processing.
    /// </summary>
    /// <returns>A RunningPlexer instance containing tasks for the demuxer and muxer.</returns>
    public RunningPlexer Spawn()
    {
        Task demuxerTask = _demuxer.RunAsync(_plexerCts.Token);
        Task muxerTask = _muxer.RunAsync(_plexerCts.Token);

        return new RunningPlexer(demuxerTask, muxerTask, _plexerCts);
    }

    /// <summary>
    /// Disposes of the plexer, releasing all resources and stopping the run loops.
    /// </summary>
    public void Dispose()
    {
        GC.SuppressFinalize(this);
        _plexerCts.Cancel(); // Signal cancellation to Demuxer and Muxer loops
        _demuxer.Dispose();
        _muxer.Dispose();
        _plexerCts.Dispose();
    }
}

/// <summary>
/// Represents the tasks running the Demuxer and Muxer, allowing for external control of the Plexer's lifecycle.
/// </summary>
/// <param name="DemuxerTask">The task running the Demuxer.</param>
/// <param name="MuxerTask">The task running the Muxer.</param>
/// <param name="PlexerCancellationTokenSource">The CancellationTokenSource used to control the Plexer.</param>
public record RunningPlexer(
    Task DemuxerTask,
    Task MuxerTask,
    CancellationTokenSource PlexerCancellationTokenSource // To allow external cancellation if needed
);