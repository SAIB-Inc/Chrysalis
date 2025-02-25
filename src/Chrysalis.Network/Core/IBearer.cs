namespace Chrysalis.Network.Core;

/// <summary>
/// Represents an abstract network bearer using functional asynchronous effects.
/// </summary>
public interface IBearer : IDisposable
{
    /// <summary>
    /// Sends data asynchronously as a functional effect.
    /// </summary>
    /// <param name="data">The data to send.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>An Aff monad that yields Unit upon completion.</returns>
    Aff<Unit> Send(byte[] data, CancellationToken cancellationToken);

    /// <summary>
    /// Receives exactly the specified number of bytes asynchronously as a functional effect.
    /// </summary>
    /// <param name="len">The exact number of bytes to receive.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>An Aff monad that yields the received byte array.</returns>
    Aff<byte[]> ReceiveExact(int len, CancellationToken cancellationToken);
}