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
    private readonly Pipe _bearer = new();
    private readonly Channel<ReadOnlyMemory<byte>> _incoming = System.Threading.Channels.Channel.CreateUnbounded<ReadOnlyMemory<byte>>();
    private readonly SemaphoreSlim _writeLock = new(1, 1);

    internal ProtocolReplay(ProtocolType protocol, params byte[][] segments)
    {
        Channel = new AgentChannel(protocol, _outgoing.Writer, _bearer.Writer, _incoming.Reader, _writeLock);
        foreach (byte[] segment in segments)
        {
            Assert.True(_incoming.Writer.TryWrite(segment));
        }
    }

    internal AgentChannel Channel { get; }

    internal async Task<byte[]> ReadSentPayloadAsync(CancellationToken cancellationToken)
    {
        ReadResult result = await _outgoing.Reader.ReadAsync(cancellationToken);
        byte[] bytes = result.Buffer.ToArray();
        _outgoing.Reader.AdvanceTo(result.Buffer.End);
        Assert.True(bytes.Length >= 3);
        Assert.Equal((byte)Channel.ProtocolId, bytes[0]);
        Assert.Equal(bytes.Length - 3, BinaryPrimitives.ReadUInt16BigEndian(bytes.AsSpan(1, 2)));
        return bytes.AsSpan(3).ToArray();
    }

    public void Dispose()
    {
        _ = _incoming.Writer.TryComplete();
        _outgoing.Writer.Complete();
        _outgoing.Reader.Complete();
        _bearer.Writer.Complete();
        _bearer.Reader.Complete();
        _writeLock.Dispose();
    }
}
