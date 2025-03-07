using System.IO.Pipelines;
using System.Net.Sockets;

namespace Chrysalis.Network.Core;

/// <summary>
/// TCP implementation of the bearer interface for network communication.
/// </summary>
public class TcpBearer : IBearer
{
    private readonly TcpClient _client;
    private readonly NetworkStream _stream;
    private bool _isDisposed;

    /// <summary>
    /// Gets the reader for consuming data from the TCP connection.
    /// </summary>
    public PipeReader Reader { get; }

    /// <summary>
    /// Gets the writer for sending data to the TCP connection.
    /// </summary>
    public PipeWriter Writer { get; }

    private TcpBearer(TcpClient client, NetworkStream stream)
    {
        _client = client;
        _stream = stream;
        Reader = PipeReader.Create(stream);
        Writer = PipeWriter.Create(stream);
    }

    /// <summary>
    /// Creates a TCP bearer connected to the specified host and port.
    /// </summary>
    /// <param name="host">The host to connect to.</param>
    /// <param name="port">The port to connect to.</param>
    /// <returns>A connected TCP bearer.</returns>
    public static TcpBearer Create(string host, int port)
    {
        TcpClient client = new(host, port);
        NetworkStream stream = client.GetStream();
        return new TcpBearer(client, stream);
    }

    /// <summary>
    /// Creates a TCP bearer asynchronously connected to the specified host and port.
    /// </summary>
    /// <param name="host">The host to connect to.</param>
    /// <param name="port">The port to connect to.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>A connected TCP bearer.</returns>
    public static async Task<TcpBearer> CreateAsync(string host, int port, CancellationToken cancellationToken = default)
    {
        TcpClient client = new();
        await client.ConnectAsync(host, port, cancellationToken);
        NetworkStream stream = client.GetStream();
        return new TcpBearer(client, stream);
    }

    /// <summary>
    /// Disposes resources used by the bearer.
    /// </summary>
    public void Dispose()
    {
        if (_isDisposed) return;

        Reader.Complete();
        Writer.Complete();
        _stream.Dispose();
        _client.Dispose();
        
        _isDisposed = true;
        GC.SuppressFinalize(this);
    }
}