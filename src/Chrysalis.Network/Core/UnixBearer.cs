using System.IO.Pipelines;
using System.Net.Sockets;
using SocketProtocolType = System.Net.Sockets.ProtocolType;

namespace Chrysalis.Network.Core;

public class UnixBearer : IBearer
{
    private readonly Socket _socket;
    private readonly NetworkStream _stream;
    public PipeReader Reader { get; private set; }
    public PipeWriter Writer { get; private set; }

    private UnixBearer(Socket socket, NetworkStream stream, PipeReader reader, PipeWriter writer)
    {
        _socket = socket;
        _stream = stream;
        Reader = reader;
        Writer = writer;
    }

    public static async Task<UnixBearer> CreateAsync(string path)
    {
        Socket socket = new(AddressFamily.Unix, SocketType.Stream, SocketProtocolType.Unspecified);
        UnixDomainSocketEndPoint endpoint = new(path);
        await socket.ConnectAsync(endpoint);
        NetworkStream stream = new(socket, ownsSocket: true);
        PipeReader reader = PipeReader.Create(stream);
        PipeWriter writer = PipeWriter.Create(stream);

        return new UnixBearer(socket, stream, reader, writer);
    }

    public void Dispose()
    {
        _stream.Dispose();
        _socket.Dispose();
        GC.SuppressFinalize(this);
    }
}