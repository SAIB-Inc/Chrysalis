using Chrysalis.Network.Core;
using System.Buffers;
using System.Threading.Channels;

namespace Chrysalis.Network.Multiplexer;

public class Muxer(IBearer bearer, ProtocolMode muxerMode) : IDisposable
{
    private readonly DateTimeOffset _startTime = DateTimeOffset.UtcNow;
    private readonly Channel<(ProtocolType ProtocolType, ReadOnlySequence<byte> Payload)> _channel = Channel.CreateBounded<(ProtocolType, ReadOnlySequence<byte>)>(50);

    public ChannelWriter<(ProtocolType, ReadOnlySequence<byte>)> Writer => _channel.Writer;

    private async Task WriteSegmentAsync(MuxSegment segment, CancellationToken cancellationToken)
    {
        ReadOnlySequence<byte> encodedSegment = MuxSegmentCodec.Encode(segment);
        await bearer.Writer.WriteAsync(encodedSegment.First, cancellationToken);
        await bearer.Writer.FlushAsync(cancellationToken);
    }

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            (ProtocolType ProtocolId, ReadOnlySequence<byte> Payload) = await _channel.Reader.ReadAsync(cancellationToken);

            MuxSegmentHeader segmentHeader = new(
               TransmissionTime: (uint)(DateTimeOffset.UtcNow - _startTime).TotalMilliseconds,
               ProtocolId,
               PayloadLength: (ushort)Payload.Length,
               Mode: muxerMode == ProtocolMode.Responder
            );

            MuxSegment segment = new(segmentHeader, Payload);

            await WriteSegmentAsync(segment, cancellationToken);
        }
    }

    public void Dispose()
    {
        GC.SuppressFinalize(this);
        bearer.Dispose();
    }
}
