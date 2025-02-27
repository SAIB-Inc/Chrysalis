using System;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using LanguageExt;
using static LanguageExt.Prelude;
using SocketProtocolType = System.Net.Sockets.ProtocolType;

namespace Chrysalis.Network.Core
{
    /// <summary>
    /// Represents a Unix domain socket–based implementation of the IBearer interface using functional asynchronous effects.
    /// </summary>
    public class UnixBearer(Socket socket, NetworkStream stream) : BearerBase(socket, stream)
    {


        /// <summary>
        /// Asynchronous factory method to create a UnixBearer instance.
        /// Returns an Aff monad encapsulating the asynchronous connection effect.
        /// </summary>
        /// <param name="path">The Unix domain socket path.</param>
        /// <returns>An Aff monad wrapping a UnixBearer instance.</returns>
        public static Aff<UnixBearer> Create(string path) =>
            Aff(async () =>
            {
                // Create a Unix domain socket.
                var socket = new Socket(AddressFamily.Unix, SocketType.Stream, SocketProtocolType.Unspecified);
                var endpoint = new UnixDomainSocketEndPoint(path);
                await socket.ConnectAsync(endpoint);
                // Create a network stream over the socket.
                var stream = new NetworkStream(socket, ownsSocket: true);
                return new UnixBearer(socket, stream);
            });
    }
}
