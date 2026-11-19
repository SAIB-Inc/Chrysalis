using System.Buffers.Binary;
using System.Net.Sockets;
using Chrysalis.Tx.Providers;
using Xunit;

namespace Chrysalis.Tx.Test.Providers;

public class OuroborosTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ArgumentsThenCancellationThenUnsupported(bool metadata)
    {
        using CancellationTokenSource canceled = new(TimeSpan.FromSeconds(10));
        using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(10));
        await canceled.CancelAsync();
        Ouroboros provider = new("missing-test-node.sock");
        Task Invoke(string hash, CancellationToken ct) => metadata
            ? provider.GetTransactionMetadataAsync(hash, ct)
            : provider.GetUtxoByOutRefAsync(hash, 0, ct);
        await Assert.ThrowsAsync<ArgumentNullException>(() => Invoke(null!, canceled.Token));
        OperationCanceledException failure = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Invoke(BlockfrostTests.Hash, canceled.Token));
        Assert.Equal(canceled.Token, failure.CancellationToken);
        await Assert.ThrowsAsync<NotSupportedException>(() => Invoke(BlockfrostTests.Hash, timeout.Token));
        failure = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => provider.GetParametersAsync(canceled.Token));
        Assert.Equal(canceled.Token, failure.CancellationToken);
    }

    [Fact]
    public async Task CancellationClosesSocketAfterAcceptedHandshake()
    {
        using CancellationTokenSource deadline = new(TimeSpan.FromSeconds(10));
        using CancellationTokenSource caller = CancellationTokenSource.CreateLinkedTokenSource(deadline.Token);
        DirectoryInfo directory = Directory.CreateTempSubdirectory("t9");
        string path = Path.Combine(directory.FullName, "n.sock");
        try
        {
            using Socket listener = new(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
            listener.Bind(new UnixDomainSocketEndPoint(path));
            listener.Listen(1);
            Task call = new Ouroboros(path).GetParametersAsync(caller.Token);
            using Socket node = await listener.AcceptAsync(deadline.Token);
            using NetworkStream stream = new(node);
            byte[] header = new byte[8];
            await ReadSegmentAsync(stream, header, deadline.Token);
            Assert.Equal(0, BinaryPrimitives.ReadUInt16BigEndian(header.AsSpan(4)));
            // Multiplexer responder bit, handshake protocol 0; [1, 32784, [2, false]].
            await stream.WriteAsync(Convert.FromHexString("000000008000000883011980108202F4"), deadline.Token);
            await ReadSegmentAsync(stream, header, deadline.Token);
            Assert.Equal(7, BinaryPrimitives.ReadUInt16BigEndian(header.AsSpan(4)));
            await caller.CancelAsync();
            OperationCanceledException error = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => call);
            Assert.Equal(caller.Token, error.CancellationToken);
            Assert.Null(error.InnerException);
            while (await stream.ReadAsync(header, deadline.Token) != 0)
            {
                // Any already queued protocol bytes can precede the close.
            }
            Assert.False(deadline.IsCancellationRequested);
        }
        finally { directory.Delete(recursive: true); }
    }

    private static async Task ReadSegmentAsync(Stream stream, byte[] header, CancellationToken cancellationToken)
    {
        await stream.ReadExactlyAsync(header, cancellationToken);
        byte[] body = new byte[BinaryPrimitives.ReadUInt16BigEndian(header.AsSpan(6))];
        await stream.ReadExactlyAsync(body, cancellationToken);
    }
}
