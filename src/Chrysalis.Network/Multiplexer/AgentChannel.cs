using System.Buffers;
using System.Runtime.CompilerServices;
using System.Threading.Channels;
using Chrysalis.Network.Core;

namespace Chrysalis.Network.Multiplexer;

/// <summary>
/// Represents a bidirectional communication channel for a specific protocol.
/// </summary>
/// <remarks>
/// AgentChannel acts as a thin wrapper around the multiplexer's channels,
/// providing protocol-specific read and write operations.
/// </remarks>
/// <remarks>
/// Initializes a new instance of the <see cref="AgentChannel"/> class.
/// </remarks>
/// <param name="protocolId">The protocol ID this channel handles.</param>
/// <param name="plexerWriter">The writer for sending outbound messages to the multiplexer.</param>
/// <param name="plexerReader">The reader for receiving inbound messages from the demultiplexer.</param>
/// <exception cref="ArgumentNullException">Thrown if plexerWriter or plexerReader is null.</exception>
public sealed class AgentChannel(
    ProtocolType protocolId,
    ChannelWriter<(ProtocolType ProtocolId, ReadOnlySequence<byte> Payload)> plexerWriter,
    ChannelReader<ReadOnlySequence<byte>> plexerReader
)
{
    /// <summary>
    /// Enqueues a chunk of data to be sent through this protocol channel.
    /// </summary>
    /// <param name="chunk">The data to send.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>A task that completes when the data has been queued for sending.</returns>
    /// <exception cref="ChannelClosedException">Thrown if the channel has been completed.</exception>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ValueTask EnqueueChunkAsync(ReadOnlySequence<byte> chunk, CancellationToken cancellationToken = default)
    {
        // Fast path - try to write synchronously first to avoid task allocation
        if (plexerWriter.TryWrite((protocolId, chunk)))
        {
            return ValueTask.CompletedTask;
        }
        
        // Fall back to async path if channel is full
        return plexerWriter.WriteAsync((protocolId, chunk), cancellationToken);
    }

    /// <summary>
    /// Reads the next available chunk of data from this protocol channel.
    /// </summary>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The received data.</returns>
    /// <exception cref="ChannelClosedException">Thrown if the channel has been completed.</exception>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ValueTask<ReadOnlySequence<byte>> ReadChunkAsync(CancellationToken cancellationToken = default) =>
        plexerReader.ReadAsync(cancellationToken);
}