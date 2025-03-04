using System.Buffers;

namespace Chrysalis.Network.Core;

/// <summary>
/// Represents an abstract network bearer using functional asynchronous effects.
/// </summary>
public interface IBearer : IDisposable
{
    Task SendAsync(ReadOnlySequence<byte> data, CancellationToken cancellationToken);

    Task<ReadOnlySequence<byte>> ReceiveExactAsync(int len, CancellationToken cancellationToken);
}