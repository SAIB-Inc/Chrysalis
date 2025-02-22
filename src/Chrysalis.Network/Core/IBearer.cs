namespace Chrysalis.Network.Core;

public interface IBearer : IDisposable
{
    Task SendAsync(byte[] data, CancellationToken cancellationToken);
    Task<byte[]> ReceiveExactAsync(int len, CancellationToken cancellationToken);
}