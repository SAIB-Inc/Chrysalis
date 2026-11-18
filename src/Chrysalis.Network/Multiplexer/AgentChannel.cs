using System.Buffers;
using System.IO.Pipelines;
using System.Runtime.CompilerServices;
using System.Threading.Channels;
using Chrysalis.Network.Core;

namespace Chrysalis.Network.Multiplexer;

/// <summary>
/// Represents a bidirectional communication channel for a specific protocol.
/// </summary>
/// <remarks>
/// Read path consumes queue items: Demuxer/Plexer items hold complete messages; caller-made items follow their producer.
/// Caller-made channels must not change an item after queuing it: received messages may keep slices of it.
/// All sends serialize on the bearer write lock and flush the bearer directly.
/// </remarks>
public sealed class AgentChannel
{
    private const int MaxRawBatchBytes = ProtocolConstants.SegmentHeaderSize + ProtocolConstants.MaxSegmentPayloadLength;
    private static readonly ConditionalWeakTable<SemaphoreSlim, Connection> StandaloneConnections = [];
    private readonly PipeWriter _bearerWriter;
    private readonly ChannelReader<ReadOnlyMemory<byte>> _demuxReader;
    private readonly Connection _connection;
    internal bool WholeMessages { get; init; }
    internal bool IsResponder { get; }

    /// <summary>
    /// Creates a channel with a caller-owned writer and reader. Channels for the same bearer must
    /// share its write lock (and thereby share their send state). The caller owns transport disposal.
    /// A send fault is permanent for the lock; use a new lock for a new bearer.
    /// </summary>
    public AgentChannel(ProtocolType protocolId, PipeWriter bearerWriter,
        ChannelReader<ReadOnlyMemory<byte>> demuxReader, SemaphoreSlim bearerWriteLock, ProtocolMode mode = ProtocolMode.Initiator)
        : this(protocolId, bearerWriter, demuxReader,
            GetStandaloneConnection(bearerWriter, demuxReader, bearerWriteLock), mode) => WholeMessages = false;

    private static Connection GetStandaloneConnection(PipeWriter bearerWriter,
        ChannelReader<ReadOnlyMemory<byte>> demuxReader, SemaphoreSlim bearerWriteLock)
    {
        ArgumentNullException.ThrowIfNull(bearerWriter);
        ArgumentNullException.ThrowIfNull(demuxReader);
        ArgumentNullException.ThrowIfNull(bearerWriteLock);
        return StandaloneConnections.GetValue(bearerWriteLock, static gate => new Connection(null, gate));
    }

    internal AgentChannel(ProtocolType protocolId, PipeWriter bearerWriter,
        ChannelReader<ReadOnlyMemory<byte>> demuxReader, Connection connection, ProtocolMode mode)
    {
        ProtocolId = protocolId;
        _bearerWriter = bearerWriter;
        _demuxReader = demuxReader;
        _connection = connection;
        IsResponder = mode == ProtocolMode.Responder;
        WholeMessages = true;
    }

    /// <summary>
    /// Frames and writes one payload directly to the bearer.
    /// </summary>
    /// <remarks>
    /// Caller cancellation is honored while waiting for the write permit and immediately before advancing
    /// the writer. After that commit point, the flush is not cancellable by the caller. A failed flush
    /// ends the connection and does not guarantee that committed bytes were not delivered.
    /// </remarks>
    public Task EnqueueChunkAsync(ReadOnlySequence<byte> chunk, CancellationToken cancellationToken = default) =>
        SendAsync(chunk, default, null, cancellationToken);

    /// <summary>
    /// Writes a pre-encoded segment unchanged. Caller cancellation is honored up to the write, not during the flush.
    /// </summary>
    public Task WriteRawSegmentAsync(ReadOnlyMemory<byte> preEncodedSegment, CancellationToken cancellationToken = default) =>
        SendAsync(default, preEncodedSegment, 1, cancellationToken);

    /// <summary>
    /// Writes N copies of a pre-encoded segment in a single lock acquisition and flush.
    /// After the first write, caller cancellation no longer interrupts the batch; connection end can stop it.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">
    /// The batch exceeds 65,543 bytes including headers (the size of one maximum-size wire segment).
    /// No bytes are committed.
    /// </exception>
    public Task WriteBatchRawSegmentsAsync(ReadOnlyMemory<byte> preEncodedSegment, int count, CancellationToken cancellationToken = default) =>
        SendAsync(default, preEncodedSegment, count, cancellationToken, isBatch: true);

    private async Task SendAsync(ReadOnlySequence<byte> chunk, ReadOnlyMemory<byte> raw, int? count,
        CancellationToken cancellationToken, bool isBatch = false)
    {
        _connection.ThrowIfEnded();
        cancellationToken.ThrowIfCancellationRequested();
        if (!count.HasValue && chunk.Length > ProtocolConstants.MaxSegmentPayloadLength)
        {
            throw new ArgumentOutOfRangeException(nameof(chunk));
        }
        if (isBatch && ((long)raw.Length * count.GetValueOrDefault()) > MaxRawBatchBytes)
        {
            throw new ArgumentOutOfRangeException(nameof(count), count, $"The raw batch exceeds {MaxRawBatchBytes} bytes.");
        }

        await _connection.WriteLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            _connection.ThrowIfEnded();
            cancellationToken.ThrowIfCancellationRequested();
            if (count is int copies) { CommitRaw(raw, copies); }
            else { CommitMuxed(chunk, cancellationToken); }

            FlushResult result;
            try { result = await _bearerWriter.FlushAsync(CancellationToken.None).ConfigureAwait(false); }
            catch (Exception cause) { throw _connection.FailSend(cause); }
            _connection.CheckFlush(result);
        }
        finally
        {
            _ = _connection.WriteLock.Release();
        }
    }

    private void CommitRaw(ReadOnlyMemory<byte> raw, int copies)
    {
        if (copies <= 0 || raw.IsEmpty) { return; }
        ReadOnlySpan<byte> segment = raw.Span;
        for (int i = 0; i < copies; i++)
        {
            _connection.ThrowIfEnded();
            try { _bearerWriter.Write(segment); }
            catch (Exception cause) { throw _connection.FailSend(cause); }
        }
    }

    private void CommitMuxed(ReadOnlySequence<byte> chunk, CancellationToken cancellationToken)
    {
        int size = ProtocolConstants.SegmentHeaderSize + (int)chunk.Length;
        Span<byte> frame;
        try { frame = _bearerWriter.GetSpan(size); }
        catch (Exception cause) { throw _connection.FailSend(cause); }

        MuxSegmentCodec.WriteHeader(frame, _connection.TransmissionTime, ProtocolId, (ushort)chunk.Length, IsResponder);
        // A caller's memory can throw on access. Do not end the connection or advance the writer.
        chunk.CopyTo(frame[ProtocolConstants.SegmentHeaderSize..]);
        cancellationToken.ThrowIfCancellationRequested();
        try { _bearerWriter.Advance(size); }
        catch (Exception cause) { throw _connection.FailSend(cause); }
    }

    /// <summary>
    /// Reads the next queue item from this protocol channel.
    /// </summary>
    /// <remarks>Demuxer/Plexer items contain the whole messages one segment completed, or empty memory.
    /// Caller-provided readers define their own items and completion exceptions; this method forwards them unchanged.</remarks>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ValueTask<ReadOnlyMemory<byte>> ReadSegmentAsync(CancellationToken cancellationToken = default) => _demuxReader.ReadAsync(cancellationToken);

    /// <summary>
    /// Gets the protocol ID for this channel.
    /// </summary>
    public ProtocolType ProtocolId { get; }
}
