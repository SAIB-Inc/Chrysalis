using System.IO.Pipelines;
using System.Net.Sockets;
using SocketProtocolType = System.Net.Sockets.ProtocolType;

namespace Chrysalis.Network.Core;

/// <summary>
/// Unix domain socket implementation of the bearer interface.
/// </summary>
public sealed class UnixBearer : IBearer
{
    private readonly Socket _socket;
    private readonly NetworkStream _stream;
    private bool _isDisposed;

    /// <summary>
    /// Gets the reader for consuming data from the Unix socket.
    /// </summary>
    public PipeReader Reader { get; }

    /// <summary>
    /// Gets the writer for sending data to the Unix socket.
    /// </summary>
    public PipeWriter Writer { get; }

    private UnixBearer(Socket socket, NetworkStream stream)
    {
        _socket = socket;
        _stream = stream;
        ConfigureSocket(socket);
        Reader = PipeReader.Create(stream, new StreamPipeReaderOptions(bufferSize: 65_536, minimumReadSize: 32_768));
        Writer = PipeWriter.Create(stream, new StreamPipeWriterOptions(minimumBufferSize: 4_096));
    }

    /// <summary>
    /// Configures socket options for optimal throughput.
    /// </summary>
    private static void ConfigureSocket(Socket socket)
    {
        socket.SendBufferSize = 131_072;
        socket.ReceiveBufferSize = 262_144;
    }

    /// <summary>
    /// Creates a Unix domain socket bearer connected to the specified path.
    /// </summary>
    /// <param name="path">The socket path to connect to.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>A connected Unix socket bearer.</returns>
    public static async Task<UnixBearer> CreateAsync(string path, CancellationToken cancellationToken)
    {
        Socket socket = new(AddressFamily.Unix, SocketType.Stream, SocketProtocolType.Unspecified);
        UnixDomainSocketEndPoint endpoint = new(path);
        await socket.ConnectAsync(endpoint, cancellationToken).ConfigureAwait(false);
        NetworkStream stream = new(socket, ownsSocket: true);
        return new UnixBearer(socket, stream);
    }

    /// <summary>
    /// Disposes resources used by the bearer.
    /// </summary>
    public void Dispose()
    {
        if (_isDisposed)
        {
            return;
        }

        Reader.Complete();
        Writer.Complete();
        _stream.Dispose();
        _socket.Dispose();

        _isDisposed = true;
    }
}
