using System.Buffers;
using System.Runtime.ExceptionServices;
using SAIB.Cbor;

namespace Chrysalis.Network.Multiplexer;

/// <summary>Scans once, retaining only an unfinished frame. Output is never reused.</summary>
internal sealed class MessageAssembler(int? limit = null) : IDisposable
{
    private CborFrameScanner.State _scanner;
    private byte[] _staging = [];
    private int _staged;
    private Exception? _failure;

    internal bool HasPartial => _scanner.BytesScanned != 0;

    internal ReadOnlyMemory<byte> Append(ReadOnlyMemory<byte> input, bool callerOwned = false) =>
        Append(new ReadOnlySequence<byte>(input), callerOwned ? input : (ReadOnlyMemory<byte>?)null);

    internal ReadOnlyMemory<byte> Append(in ReadOnlySequence<byte> input) => Append(input, null);

    private ReadOnlyMemory<byte> Append(in ReadOnlySequence<byte> input, ReadOnlyMemory<byte>? owned)
    {
        ThrowIfFailed();
        int offset = 0;
        int completed = 0;
        foreach (ReadOnlyMemory<byte> memory in input)
        {
            ReadOnlySpan<byte> span = memory.Span;
            while (!span.IsEmpty)
            {
                int result = _scanner.Scan(span, out int used);
                if (limit is int cap && _scanner.BytesScanned > cap) { throw Fail(new InvalidDataException("Mini-protocol message exceeds its size limit.")); }
                if (result < 0) { throw Fail(new CborException("Invalid CBOR frame.")); }
                offset += used;
                span = span[used..];
                if (result > 0)
                {
                    completed = offset;
                    _scanner.Dispose();
                }
            }
        }
        ReadOnlyMemory<byte> item = default;
        if (completed != 0)
        {
            if (_staged == 0 && owned is ReadOnlyMemory<byte> caller) { item = caller[..completed]; }
            else
            {
                byte[] output = GC.AllocateUninitializedArray<byte>(checked(_staged + completed));
                _staging.AsSpan(0, _staged).CopyTo(output);
                input.Slice(0, completed).CopyTo(output.AsSpan(_staged));
                item = output;
            }
            _staged = 0;
        }
        int tail = checked((int)input.Length - completed);
        if (tail != 0)
        {
            int required = checked(_staged + tail);
            if (_staging.Length < required)
            {
                int capacity = (int)Math.Min(Array.MaxLength, Math.Max(required, Math.Max(256L, _staging.Length * 2L)));
                Array.Resize(ref _staging, capacity);
            }
            input.Slice(completed).CopyTo(_staging.AsSpan(_staged));
            _staged = required;
        }
        return item;
    }

    internal void ThrowIfFailed()
    {
        if (_failure is not null) { ExceptionDispatchInfo.Throw(_failure); }
    }

    internal Exception Fail(Exception error)
    {
        _failure ??= error;
        _scanner.Dispose();
        return _failure;
    }

    public void Dispose() => _scanner.Dispose();
}
