using System.Buffers;
using System.Net.Sockets;
using SArray = System.Array;
namespace Chrysalis.Network.Core
{
    /// <summary>
    /// Represents a Unix domain socket–based implementation of the IBearer interface using functional asynchronous effects.
    /// </summary>
    public class BearerBase(Socket socket, NetworkStream stream) : IBearer
    {
        private readonly Socket _socket = socket;
        private readonly NetworkStream _stream = stream;

        /// <summary>
        /// Sends data asynchronously over the network stream.
        /// Returns an Aff monad that encapsulates the effect.
        /// </summary>
        /// <param name="data">The byte array to send.</param>
        /// <param name="cancellationToken">Cancellation token to cancel the operation.</param>
        /// <returns>An Aff monad yielding Unit upon completion.</returns>
        public Aff<Unit> Send(ReadOnlyMemory<byte> data, CancellationToken cancellationToken) =>
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
        public Aff<ReadOnlyMemory<byte>> Receive(int len, CancellationToken cancellationToken) =>
            Aff(async () =>
            {
                if (len == 0) return ReadOnlyMemory<byte>.Empty;

                if (_stream.DataAvailable)
                {
                    byte[] buffer = new byte[len];
                    await _stream.ReadExactlyAsync(buffer.AsMemory(0, len), cancellationToken);
                    return buffer.AsMemory(0, len);
                }

                return ReadOnlyMemory<byte>.Empty;
            });

        /// <summary>
        /// Disposes the UnixBearer by releasing the network stream and socket resources.
        /// </summary>
        public void Dispose()
        {
            _stream.Dispose();
            _socket.Dispose();
            GC.SuppressFinalize(this);
        }
    }
}
