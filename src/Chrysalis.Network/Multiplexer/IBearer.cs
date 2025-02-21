namespace Chrysalis.Network.Multiplexer;

public interface IBearer : IDisposable
{
    Task SendAsync(byte[] data, CancellationToken cancellationToken);
    Task<byte[]> ReceiveAsync(CancellationToken cancellationToken);
}