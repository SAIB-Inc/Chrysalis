using System.IO.Pipelines;
using System.Net.Sockets;

namespace Chrysalis.Network.Core;

public class TcpBearer : IBearer
{
    private readonly TcpClient _client;
    private readonly NetworkStream _stream;
    public PipeReader Reader { get; private set; }
    public PipeWriter Writer { get; private set; }

    private TcpBearer(TcpClient client, NetworkStream stream, PipeReader reader, PipeWriter writer)
    {
        _client = client;
        _stream = stream;
        Reader = reader;
        Writer = writer;
    }

    public static TcpBearer Create(string host, int port)
    {
        TcpClient client = new(host, port);
        NetworkStream stream = client.GetStream();
        PipeReader reader = PipeReader.Create(stream);
        PipeWriter writer = PipeWriter.Create(stream);

        return new TcpBearer(client, stream, reader, writer);
    }

    public void Dispose()
    {
        _stream.Dispose();
        _client.Dispose();
        GC.SuppressFinalize(this);
    }
}