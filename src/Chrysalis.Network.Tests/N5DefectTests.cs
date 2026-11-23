using System.Buffers;
using System.Buffers.Binary;
using System.IO.Pipelines;
using System.Net;
using System.Net.Sockets;
using Chrysalis.Codec.Types;
using Chrysalis.Network.Cbor.BlockFetch;
using Chrysalis.Network.Cbor.ChainSync;
using Chrysalis.Network.Cbor.Handshake;
using Chrysalis.Network.Core;
using Chrysalis.Network.MiniProtocols;
using Chrysalis.Network.Multiplexer;
using Xunit;
using ProtocolType = Chrysalis.Network.Core.ProtocolType;

namespace Chrysalis.Network.Tests;

internal static class N5Wire
{
    /// <summary>A guard against a hang only. No test asserts on time.</summary>
    internal static readonly TimeSpan Deadline = TimeSpan.FromSeconds(5);

    /// <summary>Writes one segment from the peer, framed as B1CarbonNodeClientTests.cs:205-213 frames a handshake reply.</summary>
    internal static async Task ReplyAsync(SendBearer bearer, ProtocolType protocol, byte[] payload)
    {
        byte[] segment = new byte[ProtocolConstants.SegmentHeaderSize + payload.Length];
        BinaryPrimitives.WriteUInt16BigEndian(segment.AsSpan(4), (ushort)(0x8000 | (ushort)protocol));
        BinaryPrimitives.WriteUInt16BigEndian(segment.AsSpan(6), checked((ushort)payload.Length));
        payload.CopyTo(segment, ProtocolConstants.SegmentHeaderSize);
        _ = await bearer.Input.WriteAsync(segment, CancellationToken.None);
    }

    /// <summary>A loopback port that was listening a moment ago and is closed now.</summary>
    internal static int ClosedPort()
    {
        using TcpListener listener = new(IPAddress.Loopback, 0);
        listener.Start();
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }
}

public sealed class N5DefectTests
{
    [Fact]
    public async Task ChainSyncReadsTheOwedReplyAfterACancelledWait()
    {
        using SendBearer bearer = new();
        using Plexer plexer = new(bearer);
        ChainSync sync = new(plexer.SubscribeClient(ProtocolType.ClientChainSync));
        Task run = plexer.RunAsync(CancellationToken.None);
        using CancellationTokenSource cancel = new();
        try
        {
            Task<MessageNextResponse?> first = sync.NextRequestAsync(cancel.Token);
            await cancel.CancelAsync();
            _ = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => first);
            Assert.Equal(ChainSyncState.CanAwait, sync.State);

            await N5Wire.ReplyAsync(bearer, ProtocolType.ClientChainSync, Convert.FromHexString("8101"));

            MessageNextResponse? next = await sync.NextRequestAsync(CancellationToken.None).WaitAsync(N5Wire.Deadline, CancellationToken.None);
            _ = Assert.IsType<MessageAwaitReply>(next);
            Assert.Equal(ChainSyncState.MustReply, sync.State);
            Assert.Equal(ProtocolConstants.SegmentHeaderSize + 2, bearer.Output.Bytes.Length);
        }
        finally
        {
            plexer.Dispose();
            _ = await Record.ExceptionAsync(() => run);
        }
    }

    [Fact]
    public async Task ChainSyncThrowsAfterTheConnectionEnds()
    {
        using SendBearer bearer = new();
        using Plexer plexer = new(bearer);
        ChainSync sync = new(plexer.SubscribeClient(ProtocolType.ClientChainSync));
        Task run = plexer.RunAsync(CancellationToken.None);
        try
        {
            Task<MessageNextResponse?> first = sync.NextRequestAsync(CancellationToken.None);
            plexer.Dispose();
            _ = await Assert.ThrowsAsync<ConnectionClosedException>(() => first);
            _ = await Assert.ThrowsAsync<ConnectionClosedException>(() => sync.NextRequestAsync(CancellationToken.None));
        }
        finally
        {
            plexer.Dispose();
            _ = await Record.ExceptionAsync(() => run);
        }
    }

    [Fact]
    public async Task ASendCancelledAfterItsFirstSegmentEndsTheConnection()
    {
        const int frame = ProtocolConstants.SegmentHeaderSize + ProtocolConstants.MaxSegmentPayloadLength;
        using SendBearer bearer = new(new SendWriter(2 * frame));
        using Plexer plexer = new(bearer);
        AgentChannel channel = plexer.SubscribeClient(ProtocolType.BlockFetch);
        ChannelBuffer buffer = new(channel);
        TaskCompletionSource<FlushResult> stalled = DirectSendShutdownTests.ParkFlush(bearer.Output);
        // Two segments, as ChannelBufferCancellationTests.cs:36.
        BlockBody message = new(4, new CborEncodedValue(new byte[ProtocolConstants.MaxSegmentPayloadLength + 1]));
        using CancellationTokenSource cancel = new();
        Task send = buffer.SendFullMessageAsync(message, cancel.Token);
        Assert.False(send.IsCompleted);                     // the first segment's flush is parked
        await cancel.CancelAsync();
        bearer.Output.AsyncFlush = null;
        stalled.SetResult(default);                         // the first segment is on the wire
        OperationCanceledException cancelled = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => send);
        Assert.Equal(frame, bearer.Output.Bytes.Length);    // no second segment
        ConnectionClosedException closed = await Assert.ThrowsAsync<ConnectionClosedException>(
            () => channel.EnqueueChunkAsync(new ReadOnlySequence<byte>(new byte[1]), CancellationToken.None));
        IOException cause = Assert.IsType<IOException>(closed.InnerException);
        Assert.Same(cancelled, cause.InnerException);
    }

    [Fact]
    public async Task PeerClientCanBeDisposedTwice()
    {
        using SendBearer bearer = new();
        using PeerClient client = PeerClient.CreateFromBearer(bearer);
        client.Replies.Clock = new ManualClock();
        Task start = client.StartAsync(42, TimeSpan.FromHours(1), CancellationToken.None);
        await N5Wire.ReplyAsync(bearer, ProtocolType.Handshake, Convert.FromHexString("83010e84182af500f4"));
        await start.WaitAsync(N5Wire.Deadline, CancellationToken.None);

        Assert.Null(Record.Exception(client.Dispose));
        Assert.Null(Record.Exception(client.Dispose));
    }

    [Fact]
    public async Task AFailedPeerStartEndsTheConnection()
    {
        using SendBearer bearer = new();
        using PeerClient client = PeerClient.CreateFromBearer(bearer);
        client.Replies.Clock = new ManualClock();
        Task start = client.StartAsync(42, TimeSpan.FromHours(1), CancellationToken.None);
        await N5Wire.ReplyAsync(bearer, ProtocolType.Handshake, Convert.FromHexString("8202830219801463626164"));
        _ = await Assert.ThrowsAsync<HandshakeRefusedException>(() => start.WaitAsync(N5Wire.Deadline, CancellationToken.None));
        _ = await Assert.ThrowsAsync<ConnectionClosedException>(() => client.ChainSync.DoneAsync(CancellationToken.None));
    }

    [Fact]
    public async Task AFailedUnixConnectDisposesItsSocket()
    {
        using Socket socket = new(AddressFamily.Unix, SocketType.Stream, System.Net.Sockets.ProtocolType.Unspecified);
        _ = await Assert.ThrowsAnyAsync<SocketException>(
            () => UnixBearer.ConnectOrDisposeAsync(socket, new UnixDomainSocketEndPoint("/nonexistent/chrysalis-n5.sock"), CancellationToken.None));
        Assert.True(socket.SafeHandle.IsClosed);
    }

    [Fact]
    public async Task AFailedTcpConnectDisposesItsClient()
    {
        using TcpClient client = new();
        Socket socket = client.Client;
        _ = await Assert.ThrowsAnyAsync<SocketException>(
            () => TcpBearer.ConnectOrDisposeAsync(client, "127.0.0.1", N5Wire.ClosedPort(), CancellationToken.None));
        Assert.True(socket.SafeHandle.IsClosed);
    }
}
