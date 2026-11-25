using System.Buffers;
using System.Runtime.CompilerServices;
using System.Threading.Channels;
using System.Threading.Tasks.Sources;
using Chrysalis.Codec.Serialization;
using Chrysalis.Codec.Types;
using Chrysalis.Network.Core;

namespace Chrysalis.Network.Multiplexer;

/// <summary>
/// Provides buffer management for sending and receiving complete CBOR messages over an AgentChannel.
/// </summary>
/// <remarks>
/// Every returned message owns its bytes, as part of an item this buffer never reuses.
/// Caller-made channels must not modify items after queuing them; a message can be a slice of one.
/// </remarks>
public sealed class ChannelBuffer(AgentChannel channel)
{
    private readonly AgentChannel _channel = channel;
    private ReadOnlyMemory<byte> _cursor;
    private readonly MessageAssembler? _assembler = channel.WholeMessages ? null : new(ProtocolLimits.Get(channel.ProtocolId));
    private bool _failed;
    private int _receiving;
    private object? _waiter;

    /// <summary>
    /// Writes a pre-encoded mux segment directly to the bearer, bypassing serialization and muxer.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Task SendPreEncodedSegmentAsync(ReadOnlyMemory<byte> preEncodedSegment, CancellationToken cancellationToken) => _channel.WriteRawSegmentAsync(preEncodedSegment, cancellationToken);

    /// <summary>
    /// Writes N copies of a pre-encoded mux segment in a single batched write.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Task SendPreEncodedSegmentBatchAsync(ReadOnlyMemory<byte> preEncodedSegment, int count, CancellationToken cancellationToken) => _channel.WriteBatchRawSegmentsAsync(preEncodedSegment, count, cancellationToken);

    /// <summary>
    /// Sends a complete CBOR message, chunked into segments if necessary.
    /// A cancellation after the first segment of a longer message ends the connection.
    /// </summary>
    /// <exception cref="OperationCanceledException">The token is cancelled before the send, or before a later segment of a longer message is written.</exception>
    public async Task SendFullMessageAsync<T>(T message, CancellationToken cancellationToken) where T : CborRecord
    {
        cancellationToken.ThrowIfCancellationRequested();
        ReadOnlyMemory<byte> payloadMemory = CborSerializer.SerializeToMemory(message);
        int payloadLength = payloadMemory.Length;

        for (int offset = 0; offset < payloadLength; offset += ProtocolConstants.MaxSegmentPayloadLength)
        {
            int chunkSize = Math.Min(ProtocolConstants.MaxSegmentPayloadLength, payloadLength - offset);
            ReadOnlyMemory<byte> chunkMemory = payloadMemory.Slice(offset, chunkSize);
            ReadOnlySequence<byte> chunkSequence = new(chunkMemory);
            try { await _channel.EnqueueChunkAsync(chunkSequence, cancellationToken).ConfigureAwait(false); }
            // Part of the message is on the wire and the rest will not follow: nothing valid can be sent after it.
            catch (OperationCanceledException error) when (offset > 0)
            {
                _channel.End(new IOException("A send was cancelled after part of its message was written.", error));
                throw;
            }
        }
    }

    /// <summary>
    /// Receives and deserializes the next complete CBOR message.
    /// </summary>
    /// <remarks>Demuxer/Plexer channels supply whole-message items. Caller-made channels assemble
    /// their producer's items and preserve that reader's completion errors. On a deserialization error,
    /// the receive cursor is cleared; on Demuxer/Plexer channels the connection ends with that error,
    /// and on caller-made channels the error remains sticky for later receives. At most one receive may
    /// be outstanding per buffer at a time: await the returned <see cref="ValueTask{TResult}"/> at most
    /// once and before starting the next receive, or the overlapping call faults with
    /// <see cref="InvalidOperationException"/> without disturbing the active receive. One reusable waiter
    /// is cached per current <typeparamref name="T"/> (replaced if types alternate); synchronous failures
    /// allocate a one-shot waiter (off the measured paths) so a parked receive is never disturbed.</remarks>
    /// <exception cref="InvalidOperationException">A previous receive on this buffer is still pending or has not been awaited.</exception>
    public ValueTask<T> ReceiveFullMessageAsync<T>(CancellationToken cancellationToken) where T : CborRecord
    {
        if (Interlocked.Exchange(ref _receiving, 1) != 0)
        {
            return new ReceiveWaiter<T>(this).Fail(new InvalidOperationException("Only one receive may be outstanding on a ChannelBuffer."));
        }
        try
        {
            if (!TryFillCursor(cancellationToken, out ConfiguredValueTaskAwaitable<ReadOnlyMemory<byte>>.ConfiguredValueTaskAwaiter pending))
            {
                return Waiter<T>().Wait(pending, cancellationToken);
            }
            T result = DeserializeFromCursor<T>();
            Volatile.Write(ref _receiving, 0);
            return ValueTask.FromResult(result);
        }
        catch (Exception error)
        {
            Volatile.Write(ref _receiving, 0);
            return new ReceiveWaiter<T>(this).Fail(error);
        }
    }

    private ReceiveWaiter<T> Waiter<T>() where T : CborRecord
    {
        if (_waiter is ReceiveWaiter<T> existing) { return existing; }
        ReceiveWaiter<T> created = new(this, clearsReceiving: true);
        _waiter = created;
        return created;
    }

    private bool TryFillCursor(
        CancellationToken cancellationToken,
        out ConfiguredValueTaskAwaitable<ReadOnlyMemory<byte>>.ConfiguredValueTaskAwaiter pending)
    {
        while (_cursor.IsEmpty)
        {
            if (_failed) { _channel.ThrowIfEnded(); }
            _assembler?.ThrowIfFailed();
            ValueTask<ReadOnlyMemory<byte>> read;
            try { read = _assembler is null ? _channel.ReadMessageAsync(cancellationToken) : _channel.ReadSegmentAsync(cancellationToken); }
            catch (ChannelClosedException error) { _ = _assembler?.Fail(error); throw; }
            ConfiguredValueTaskAwaitable<ReadOnlyMemory<byte>>.ConfiguredValueTaskAwaiter awaiter = read.ConfigureAwait(false).GetAwaiter();
            if (!awaiter.IsCompleted)
            {
                pending = awaiter;
                return false;
            }
            CompleteRead(awaiter);
        }
        pending = default;
        return true;
    }

    private void CompleteRead(ConfiguredValueTaskAwaitable<ReadOnlyMemory<byte>>.ConfiguredValueTaskAwaiter awaiter)
    {
        ReadOnlyMemory<byte> item;
        try { item = awaiter.GetResult(); }
        catch (ChannelClosedException error) { _ = _assembler?.Fail(error); throw; }
        _cursor = _assembler is null ? item : _assembler.Append(item, callerOwned: true);
    }

    private T DeserializeFromCursor<T>() where T : CborRecord
    {
        T result;
        int consumed;
        try { result = CborSerializer.Deserialize<T>(_cursor, out consumed); }
        catch (Exception error)
        {
            _cursor = default;
            if (_assembler is null)
            {
                _failed = true;
                _channel.End(error);
            }
            else
            {
                _ = _assembler.Fail(error);
            }
            throw;
        }
        _cursor = _cursor[consumed..];
        return result;
    }

    private sealed class ReceiveWaiter<T> : IValueTaskSource<T> where T : CborRecord
    {
        private readonly ChannelBuffer _owner;
        private readonly bool _clearsReceiving;
        private readonly Action _onCompleted;
        private ManualResetValueTaskSourceCore<T> _core;
        private ConfiguredValueTaskAwaitable<ReadOnlyMemory<byte>>.ConfiguredValueTaskAwaiter _awaiter;
        private CancellationToken _cancellationToken;

        internal ReceiveWaiter(ChannelBuffer owner, bool clearsReceiving = false)
        {
            _owner = owner;
            _clearsReceiving = clearsReceiving;
            _onCompleted = Continue;
        }

        internal ValueTask<T> Wait(
            ConfiguredValueTaskAwaitable<ReadOnlyMemory<byte>>.ConfiguredValueTaskAwaiter pending,
            CancellationToken cancellationToken)
        {
            _core.Reset();
            _cancellationToken = cancellationToken;
            short token = _core.Version;
            _awaiter = pending;
            _awaiter.UnsafeOnCompleted(_onCompleted);
            return new ValueTask<T>(this, token);
        }

        internal ValueTask<T> Fail(Exception error)
        {
            _core.Reset();
            short token = _core.Version;
            _core.SetException(error);
            return new ValueTask<T>(this, token);
        }

        private void Continue()
        {
            try
            {
                _owner.CompleteRead(_awaiter);
                if (!_owner.TryFillCursor(_cancellationToken, out ConfiguredValueTaskAwaitable<ReadOnlyMemory<byte>>.ConfiguredValueTaskAwaiter pending))
                {
                    _awaiter = pending;
                    _awaiter.UnsafeOnCompleted(_onCompleted);
                    return;
                }
                T result = _owner.DeserializeFromCursor<T>();
                _awaiter = default;
                _cancellationToken = default;
                _core.SetResult(result);
            }
            catch (Exception error)
            {
                _awaiter = default;
                _cancellationToken = default;
                _core.SetException(error);
            }
        }

        T IValueTaskSource<T>.GetResult(short token)
        {
            if (_core.GetStatus(token) == ValueTaskSourceStatus.Pending)
            {
                throw new InvalidOperationException("The receive operation has not completed.");
            }
            try { return _core.GetResult(token); }
            finally
            {
                _core.Reset();
                if (_clearsReceiving) { Volatile.Write(ref _owner._receiving, 0); }
            }
        }

        ValueTaskSourceStatus IValueTaskSource<T>.GetStatus(short token) => _core.GetStatus(token);

        void IValueTaskSource<T>.OnCompleted(Action<object?> continuation, object? state, short token, ValueTaskSourceOnCompletedFlags flags) =>
            _core.OnCompleted(continuation, state, token, flags);
    }
}
