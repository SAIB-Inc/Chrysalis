using System.Net.Sockets;

namespace Chrysalis.Network.Core;

/// <summary>
/// Represents a TCP-based implementation of the IBearer interface using functional asynchronous effects.
/// </summary>
public class TcpBearer : IBearer
{
    private readonly TcpClient _client;
    private readonly NetworkStream _stream;

    /// <summary>
    /// Private constructor to enforce the use of the factory method.
    /// </summary>
    /// <param name="client">The TcpClient instance.</param>
    /// <param name="stream">The network stream associated with the client.</param>
    private TcpBearer(TcpClient client, NetworkStream stream)
    {
        _client = client;
        _stream = stream;
    }

    /// <summary>
    /// Asynchronous factory method to create a TcpBearer instance.
    /// Returns an Aff monad encapsulating the asynchronous connection effect.
    /// </summary>
    /// <param name="host">The host to connect to.</param>
    /// <param name="port">The port number to connect to.</param>
    /// <returns>An Aff monad wrapping a TcpBearer instance.</returns>
    public static Aff<TcpBearer> CreateAsync(string host, int port) =>
        Aff(async () =>
        {
            var client = new TcpClient();
            await client.ConnectAsync(host, port);
            return new TcpBearer(client, client.GetStream());
        });

    /// <summary>
    /// Sends data asynchronously over the network stream.
    /// Returns an Aff monad that encapsulates the effect.
    /// </summary>
    /// <param name="data">The byte array to send.</param>
    /// <param name="cancellationToken">Cancellation token to cancel the operation.</param>
    /// <returns>An Aff monad yielding Unit upon completion.</returns>
    public Aff<Unit> SendAsync(byte[] data, CancellationToken cancellationToken) =>
        Aff(async () =>
        {
            await _stream.WriteAsync(data, cancellationToken);
            return unit;
        });

    /// <summary>
    /// Receives exactly the specified number of bytes from the network stream asynchronously.
    /// Returns an Aff monad that encapsulates the effect.
    /// </summary>
    /// <param name="len">The exact number of bytes to receive.</param>
    /// <param name="cancellationToken">Cancellation token to cancel the operation.</param>
    /// <returns>An Aff monad yielding the received byte array.</returns>
    public Aff<byte[]> ReceiveExactAsync(int len, CancellationToken cancellationToken) =>
        Aff(async () =>
        {
            if (_stream.DataAvailable)
            {
                byte[] buffer = new byte[len];
                await _stream.ReadExactlyAsync(buffer, 0, len, cancellationToken);
                return buffer;
            }
            return [];
        });

    /// <summary>
    /// Disposes the TcpBearer by releasing the network stream and TcpClient resources.
    /// </summary>
    public void Dispose()
    {
        _stream.Dispose();
        _client.Dispose();
        GC.SuppressFinalize(this);
    }
}