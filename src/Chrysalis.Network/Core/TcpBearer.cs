using System.Net.Sockets;
using SProtocolType = System.Net.Sockets.ProtocolType;

namespace Chrysalis.Network.Core;
/// <summary>
/// Represents a Unix domain socket–based implementation of the IBearer interface using functional asynchronous effects.
/// </summary>
public class TcpBearer(Socket socket, NetworkStream stream) : BearerBase(socket, stream)
{

    /// <summary>
    /// Asynchronous factory method to create a TcpBearer instance.
    /// Returns an Aff monad encapsulating the asynchronous connection effect.
    /// </summary>
    /// <param name="host">The host to connect to.</param>
    /// <param name="port">The port number to connect to.</param>
    /// <returns>An Aff monad wrapping a TcpBearer instance.</returns>
    public static Aff<TcpBearer> Create(string host, int port) =>
        Aff(async () =>
        {
            // Create a TCP socket.
            var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, SProtocolType.Tcp);
            await socket.ConnectAsync(host, port);
            // Create a network stream over the socket.
            var stream = new NetworkStream(socket, ownsSocket: true);
            return new TcpBearer(socket, stream);
        });
}