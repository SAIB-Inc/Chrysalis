using Chrysalis.Network.Core;
using System.Threading.Channels;
using System.Buffers;
using System.Runtime.CompilerServices;

namespace Chrysalis.Network.Multiplexer;

public class AgentChannel(
    ProtocolType ProtocolId,
    ChannelWriter<(ProtocolType ProtocolId, ReadOnlySequence<byte> Payload)> plexerWriter,
    ChannelReader<ReadOnlySequence<byte>> plexerReader
)
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public async Task EnqueueChunkAsync(ReadOnlySequence<byte> chunk, CancellationToken cancellationToken)
        => await plexerWriter.WriteAsync((ProtocolId, Payload: chunk), cancellationToken);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public async Task<ReadOnlySequence<byte>> ReadChunkAsync(CancellationToken cancellationToken)
        => await plexerReader.ReadAsync(cancellationToken);
}
