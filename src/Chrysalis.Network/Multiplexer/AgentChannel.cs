using System;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using LanguageExt;
using static LanguageExt.Prelude;
using Chrysalis.Network.Core;

namespace Chrysalis.Network.Multiplexer;

/// <summary>
/// Represents an agent channel that routes protocol messages between a multiplexer and its subscribers.
/// </summary>
/// <param name="protocol">The protocol type for this channel.</param>
/// <param name="toPlexerSubject">The subject used to send messages to the multiplexer.</param>
/// <param name="fromPlexerSubject">The subject used to receive messages from the multiplexer.</param>
public class AgentChannel(ProtocolType protocol, ISubject<ProtocolMessage> toPlexerSubject, ISubject<byte[]> fromPlexerSubject)
{
    private ProtocolType Protocol { get; } = protocol;
    private IObservable<byte[]> FromPlexerPort { get; } = fromPlexerSubject.AsObservable();

    /// <summary>
    /// Enqueues a message chunk to the multiplexer as a functional effect.
    /// This method wraps the synchronous push operation in an Aff monad.
    /// </summary>
    /// <param name="chunk">The message chunk to enqueue.</param>
    /// <returns>An Aff monad yielding Unit upon completion.</returns>
    public Aff<Unit> EnqueueChunk(byte[] chunk) =>
        Aff(() =>
        {
            toPlexerSubject.OnNext(new ProtocolMessage(Protocol, chunk));
            return ValueTask.FromResult(unit);
        });

    /// <summary>
    /// Subscribes to the channel to receive message chunks.
    /// </summary>
    /// <param name="onNext">The action to perform when a message chunk is received.</param>
    /// <returns>An IDisposable representing the subscription.</returns>
    public IDisposable Subscribe(Action<byte[]> onNext) =>
        FromPlexerPort.Subscribe(onNext);
}
