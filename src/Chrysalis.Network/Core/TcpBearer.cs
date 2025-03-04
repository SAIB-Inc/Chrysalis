using System.Buffers;
using System.IO.Pipelines;
using System.Net.Sockets;

namespace Chrysalis.Network.Core;

public class TcpBearer : IBearer
{
    private readonly TcpClient _client;
    private readonly NetworkStream _stream;
    private readonly PipeReader _reader;
    private readonly PipeWriter _writer;

    private TcpBearer(TcpClient client, NetworkStream stream, PipeReader reader, PipeWriter writer)
    {
        _client = client;
        _stream = stream;
        _reader = reader;
        _writer = writer;
    }

    public static TcpBearer Create(string host, int port)
    {
        TcpClient client = new(host, port);
        NetworkStream stream = client.GetStream();
        PipeReader reader = PipeReader.Create(stream);
        PipeWriter writer = PipeWriter.Create(stream);

        return new TcpBearer(client, stream, reader, writer);
    }

    public async Task<ReadOnlySequence<byte>> ReceiveExactAsync(int len, CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            ReadResult result = await _reader.ReadAsync(cancellationToken);

            if (result.IsCanceled) throw new OperationCanceledException();

            ReadOnlySequence<byte> buffer = result.Buffer;

            if (buffer.Length >= len)
            {
                buffer = buffer.Slice(0, len);
                _reader.AdvanceTo(buffer.Start, buffer.End);
                return buffer;
            }

            if (result.IsCompleted)
                throw new EndOfStreamException("Connection closed before receiving the required number of bytes");
        }

        throw new OperationCanceledException(cancellationToken);
    }

    public async Task SendAsync(ReadOnlySequence<byte> data, CancellationToken cancellationToken)
    {
        foreach (ReadOnlyMemory<byte> segment in data)
        {
            Memory<byte> memory = _writer.GetMemory(segment.Length);
            segment.Span.CopyTo(memory.Span);
            _writer.Advance(segment.Length);
        }

        await _writer.FlushAsync(cancellationToken);
    }

    public void Dispose()
    {
        _stream.Dispose();
        _client.Dispose();
        GC.SuppressFinalize(this);
    }
}