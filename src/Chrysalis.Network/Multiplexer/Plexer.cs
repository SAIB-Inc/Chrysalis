using System;
using System.Threading;
using System.Threading.Tasks;
using LanguageExt;
using static LanguageExt.Prelude;
using Chrysalis.Network.Core;

namespace Chrysalis.Network.Multiplexer;

/// <summary>
/// Combines a Muxer and Demuxer to provide full‑duplex multiplexing/demultiplexing capability.
/// </summary>
/// <param name="bearer">The bearer connection to use for multiplexing.</param>
public class Plexer(IBearer bearer) : IDisposable
{
    private readonly Demuxer _demuxer = new(bearer);
    private readonly Muxer _muxer = new(bearer, ProtocolMode.Initiator);
    private readonly CancellationTokenSource _plexerCts = new();

    /// <summary>
    /// Subscribes an agent channel for client‑side protocol operation.
    /// </summary>
    /// <param name="protocol">The protocol type for the client channel.</param>
    /// <returns>A new AgentChannel configured for client‑side protocol usage.</returns>
    public AgentChannel SubscribeClient(ProtocolType protocol)
    {
        var toPlexerSubject = _muxer.GetMessageSubject();
        var fromPlexerSubject = _demuxer.Subscribe(protocol);
        return new AgentChannel(protocol, toPlexerSubject, fromPlexerSubject);
    }

    /// <summary>
    /// Subscribes an agent channel for server‑side protocol operation.
    /// </summary>
    /// <param name="protocol">The protocol type for the server channel.</param>
    /// <returns>A new AgentChannel configured for server‑side protocol usage.</returns>
    public AgentChannel SubscribeServer(ProtocolType protocol)
    {
        var toPlexerSubject = _muxer.GetMessageSubject();
        var fromPlexerSubject = _demuxer.Subscribe(protocol);
        return new AgentChannel(protocol, toPlexerSubject, fromPlexerSubject);
    }

    /// <summary>
    /// Runs the plexer by spawning the demuxer and muxer run loops in the background.
    /// The effect returns immediately once the run loops have been started.
    /// </summary>
    /// <returns>
    /// An Aff monad yielding Unit once the background tasks have been spawned.
    /// </returns>
    public Aff<Unit> Run() =>
        Aff(() =>
        {
            // Spawn the demuxer and muxer run loops in the background.
            _ = Task.Run(() => _demuxer.Run(_plexerCts.Token).Run().AsTask());
            _ = Task.Run(() => _muxer.Run(_plexerCts.Token).Run().AsTask());
            return ValueTask.FromResult(unit);
        });

    /// <summary>
    /// Disposes of the plexer, releasing all resources and stopping the run loops.
    /// </summary>
    public void Dispose()
    {
        GC.SuppressFinalize(this);
        _plexerCts.Cancel();
        _demuxer.Dispose();
        _muxer.Dispose();
        _plexerCts.Dispose();
    }
}
