using System.Reactive.Linq;
using System.Reactive.Subjects;
using Chrysalis.Network.Core;

namespace Chrysalis.Network.Multiplexer;

public class AgentChannel
(
    ProtocolType protocol,
    ISubject<ProtocolMessage> toPlexerSubject,
    ISubject<byte[]> fromPlexerSubject
)
{
    private ProtocolType Protocol { get; } = protocol;

    private IObservable<byte[]> FromPlexerPort { get; } = fromPlexerSubject.AsObservable();

    public Task EnqueueChunkAsync(byte[] chunk)
    {
        toPlexerSubject.OnNext(new ProtocolMessage(Protocol, chunk));
        return Task.CompletedTask;
    }

    public IDisposable Subscribe(Action<byte[]> onNext)
    {
        return FromPlexerPort.Subscribe(onNext);
    }
}