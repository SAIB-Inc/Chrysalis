using System.Buffers;
using System.Buffers.Binary;
using System.IO.Pipelines;
using System.Threading.Channels;
using Chrysalis.Network.Core;
using Chrysalis.Network.Multiplexer;
using Xunit;

namespace Chrysalis.Network.Tests;

internal sealed class ProtocolReplay : IDisposable
{
    private readonly Pipe _outgoing = new();
    private readonly Channel<ReadOnlyMemory<byte>> _incoming = System.Threading.Channels.Channel.CreateUnbounded<ReadOnlyMemory<byte>>();
    private readonly SemaphoreSlim _writeLock = new(1, 1);

    internal ProtocolReplay(ProtocolType protocol, params byte[][] segments)
    {
        Channel = new AgentChannel(protocol, _outgoing.Writer, _incoming.Reader, _writeLock);
        foreach (byte[] segment in segments)
        {
            Assert.True(_incoming.Writer.TryWrite(segment));
        }
    }

    internal AgentChannel Channel { get; }

    internal void CompleteIncoming() => Assert.True(_incoming.Writer.TryComplete());

    // Replay callers await the operation that sent the frame before inspecting this pipe.
    internal byte[] ReadSentPayload()
    {
        Assert.True(_outgoing.Reader.TryRead(out ReadResult result), "The operation sent no bearer frame.");
        ReadOnlySequence<byte> bytes = result.Buffer;
        try
        {
            const int headerSize = ProtocolConstants.SegmentHeaderSize;
            Assert.True(bytes.Length >= headerSize, "The operation sent an incomplete bearer header.");
            Span<byte> header = stackalloc byte[headerSize];
            bytes.Slice(0, headerSize).CopyTo(header);
            int size = BinaryPrimitives.ReadUInt16BigEndian(header[6..]);
            Assert.Equal(headerSize + (long)size, bytes.Length);
            Assert.Equal((ushort)Channel.ProtocolId, BinaryPrimitives.ReadUInt16BigEndian(header[4..]));
            return bytes.Slice(headerSize, size).ToArray();
        }
        finally { _outgoing.Reader.AdvanceTo(bytes.End); }
    }

    internal void AssertNothingSent()
    {
        Assert.Equal(0, _outgoing.Writer.UnflushedBytes);
        if (_outgoing.Reader.TryRead(out ReadResult result))
        {
            bool empty = result.Buffer.IsEmpty;
            _outgoing.Reader.AdvanceTo(result.Buffer.End);
            Assert.True(empty, "A version-rejected operation wrote an outgoing frame.");
        }
    }

    public void Dispose()
    {
        _ = _incoming.Writer.TryComplete();
        _outgoing.Writer.Complete();
        _outgoing.Reader.Complete();
        _writeLock.Dispose();
    }
}
