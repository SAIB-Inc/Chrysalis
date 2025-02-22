using System.Net.Sockets;

namespace Chrysalis.Network.Core;

 public class TcpBearer : IBearer, IDisposable
 {
     private readonly TcpClient _client;
     private readonly NetworkStream _stream;

     public TcpBearer(string host, int port)
     {
         _client = new TcpClient(host, port);
         _stream = _client.GetStream();
     }

     public async Task SendAsync(byte[] data, CancellationToken cancellationToken)
     {
        await _stream.WriteAsync(data, cancellationToken);
     }

     public async Task<byte[]> ReceiveExactAsync(int len, CancellationToken cancellationToken)
     {
         var buffer = new byte[len];
         var bytesRead = await _stream.ReadAsync(buffer.AsMemory(0, len), cancellationToken);
         var result = new byte[bytesRead];
         Array.Copy(buffer, result, bytesRead);
         return result;
     }

     public void Dispose()
     {
         _stream.Dispose();
         _client.Dispose();
         GC.SuppressFinalize(this);
     }
 }