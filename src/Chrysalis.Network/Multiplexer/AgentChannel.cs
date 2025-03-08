using System.Buffers;
using System.Buffers.Binary;
using System.IO.Pipelines;
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
public sealed class AgentChannel(
    ProtocolType protocolId,
    PipeWriter plexerWriter,
    PipeReader plexerReader
)
{
    /// <summary>
    /// Enqueues a chunk of data to be sent through this protocol channel.
    /// </summary>
    /// <param name="chunk">The data to send.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>A task that completes when the data has been queued for sending.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public async Task EnqueueChunkAsync(ReadOnlySequence<byte> chunk, CancellationToken cancellationToken = default)
    {
        // Get the total length of the payload
        int payloadLength = (int)chunk.Length;

        // Create buffer for header (3 bytes total: 1 for protocol ID, 2 for length)
        Span<byte> protocolMessage = stackalloc byte[3 + payloadLength];

        // Write protocol ID (1 byte)
        protocolMessage[0] = (byte)protocolId;

        // Write payload length (2 bytes) in big-endian format
        BinaryPrimitives.WriteUInt16BigEndian(protocolMessage[1..], (ushort)payloadLength);

        // Copy the payload chunk to the protocol message
        Span<byte> payloadSlice = protocolMessage.Slice(3, payloadLength);
        chunk.CopyTo(payloadSlice);

        // Write payload - handle multi-segment sequences efficiently
        plexerWriter.Write(protocolMessage);
        await plexerWriter.FlushAsync(cancellationToken);
    }

    /// <summary>
    /// Reads the next available chunk of data from this protocol channel.
    /// </summary>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The received data.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public async Task<ReadResult> ReadChunkAsync(CancellationToken cancellationToken = default) =>
        await plexerReader.ReadAsync(cancellationToken);

    /// <summary>
    /// Advances the channels reader to a position.
    /// </summary>
    /// <param name="position">The SequencePosition of the last consumed data.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void AdvanceTo(SequencePosition position) => plexerReader.AdvanceTo(position);
}