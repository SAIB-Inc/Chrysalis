using Chrysalis.Network.Core;
using System.Threading.Channels;

namespace Chrysalis.Network.Multiplexer;

public class Plexer(IBearer bearer) : IDisposable
{
    private readonly Demuxer _demuxer = new(bearer);
    private readonly Muxer _muxer = new(bearer, ProtocolMode.Initiator);

    public AgentChannel SubscribeClient(ProtocolType protocol)
    {
        ChannelWriter<(ProtocolType, System.Buffers.ReadOnlySequence<byte>)> writer = _muxer.Writer;
        ChannelReader<System.Buffers.ReadOnlySequence<byte>> reader = _demuxer.Subscribe(protocol);
        return new AgentChannel(protocol, writer, reader);
    }

    public AgentChannel SubscribeServer(ProtocolType protocol)
    {
        ChannelWriter<(ProtocolType, System.Buffers.ReadOnlySequence<byte>)> writer = _muxer.Writer;
        ChannelReader<System.Buffers.ReadOnlySequence<byte>> reader = _demuxer.Subscribe(protocol);
        return new AgentChannel(protocol, writer, reader);
    }

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        await Task.WhenAny(
            _demuxer.RunAsync(cancellationToken),
            _muxer.RunAsync(cancellationToken)
        );

        throw new Exception("Something went wrong");
    }

    public void Dispose()
    {
        GC.SuppressFinalize(this);
        _demuxer.Dispose();
        _muxer.Dispose();
    }
}
