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

    internal byte[] ReadSentPayload() => Assert.Single(ReadSentPayloads());

    // Replay callers await the operations that sent the frames before inspecting this pipe.
    internal List<byte[]> ReadSentPayloads()
    {
        Assert.True(_outgoing.Reader.TryRead(out ReadResult result), "The operation sent no bearer frame.");
        ReadOnlySequence<byte> bytes = result.Buffer;
        try
        {
            const int headerSize = ProtocolConstants.SegmentHeaderSize;
            Span<byte> header = stackalloc byte[headerSize];
            List<byte[]> payloads = [];
            while (!bytes.IsEmpty)
            {
                Assert.True(bytes.Length >= headerSize, "The operation sent an incomplete bearer header.");
                bytes.Slice(0, headerSize).CopyTo(header);
                int size = BinaryPrimitives.ReadUInt16BigEndian(header[6..]);
                Assert.True(bytes.Length >= headerSize + (long)size, "The operation sent an incomplete bearer frame.");
                Assert.Equal((ushort)Channel.ProtocolId, BinaryPrimitives.ReadUInt16BigEndian(header[4..]));
                payloads.Add(bytes.Slice(headerSize, size).ToArray());
                bytes = bytes.Slice(headerSize + size);
            }
            return payloads;
        }
        finally { _outgoing.Reader.AdvanceTo(result.Buffer.End); }
    }

    internal void AssertNothingSent() => AssertNothingSent(_outgoing, "A version-rejected operation wrote an outgoing frame.");

    internal static void AssertNothingSent(Pipe outgoing, string failureMessage)
    {
        Assert.Equal(0, outgoing.Writer.UnflushedBytes);
        if (outgoing.Reader.TryRead(out ReadResult result))
        {
            bool empty = result.Buffer.IsEmpty;
            outgoing.Reader.AdvanceTo(result.Buffer.End);
            Assert.True(empty, failureMessage);
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
