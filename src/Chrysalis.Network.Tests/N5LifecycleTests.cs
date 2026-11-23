using System.IO.Pipelines;
using System.Net.Sockets;
using Chrysalis.Network.MiniProtocols;
using Chrysalis.Network.Multiplexer;
using Xunit;
using ProtocolType = Chrysalis.Network.Core.ProtocolType;

namespace Chrysalis.Network.Tests;

public sealed class N5LifecycleTests
{
    /// <summary>A PeerClient over the bearer, started against the accept of B1CarbonVersionEqualityTests.cs:30 (version 14, magic 42).</summary>
    private static async Task<PeerClient> StartedPeerAsync(SendBearer bearer, ManualClock clock, CancellationToken startup)
    {
        PeerClient client = PeerClient.CreateFromBearer(bearer);
        try
        {
            client.Replies.Clock = clock;
            Task start = client.StartAsync(42, TimeSpan.FromHours(1), startup);
            await N5Wire.ReplyAsync(bearer, ProtocolType.Handshake, Convert.FromHexString("83010e84182af500f4"));
            await start.WaitAsync(N5Wire.Deadline, CancellationToken.None);
            return client;
        }
        catch
        {
            await client.DisposeAsync();
            throw;
        }
    }

    /// <summary>Holds the write lock with a parked flush and checks that DisposeAsync returns only once the bearer is disposed.</summary>
    private static async Task DisposeWaitsForTheBearerAsync(SendBearer bearer, IAsyncDisposable client, ChainSync sync)
    {
        // Not DirectSendShutdownTests.ParkFlush: the end of the connection must not release this flush.
        TaskCompletionSource<FlushResult> stalled = new(TaskCreationOptions.RunContinuationsAsynchronously);
        bearer.Output.AsyncFlush = () => new ValueTask<FlushResult>(stalled.Task);
        Task parked = sync.DoneAsync(CancellationToken.None);
        Task stop;
        try
        {
            Assert.False(parked.IsCompleted);     // in its flush, holding the write lock; on an ended connection it has already failed
            stop = client.DisposeAsync().AsTask();
            Assert.False(stop.IsCompleted);       // the bearer cannot be disposed while the write lock is held
            Assert.False(bearer.IsDisposed);
        }
        finally
        {
            _ = stalled.TrySetResult(default);    // a failed assertion must not leave the lock held: the teardown would wait for ever
        }
        await stop.WaitAsync(N5Wire.Deadline, CancellationToken.None);
        Assert.True(bearer.IsDisposed);
        _ = await Assert.ThrowsAsync<ConnectionClosedException>(() => parked);
    }

    private static async Task AssertHandshakeTimeoutAsync(
        SendBearer bearer,
        ManualClock clock,
        IAsyncDisposable client,
        Task start,
        Func<bool> isHealthy,
        Func<Exception?> getException)
    {
        try
        {
            ManualTimer timer = Assert.Single(clock.Timers);
            Assert.Equal(TimeSpan.FromSeconds(10), timer.Due);
            timer.Fire();
            ConnectionClosedException closed = await Assert.ThrowsAsync<ConnectionClosedException>(() => start.WaitAsync(N5Wire.Deadline, CancellationToken.None));
            ReplyTimeoutException timeout = Assert.IsType<ReplyTimeoutException>(closed.InnerException);
            Assert.Equal(ProtocolType.Handshake, timeout.Protocol);
            Assert.Equal(TimeSpan.FromSeconds(10), timeout.Limit);
            await client.DisposeAsync();
            Assert.True(bearer.IsDisposed);
            Assert.False(isHealthy());
            Assert.Same(timeout, getException());
        }
        finally
        {
            await client.DisposeAsync();
            _ = await Record.ExceptionAsync(() => start);
        }
    }

    [Theory]
    [InlineData("node")]
    [InlineData("peer")]
    public async Task ASilentPeerFailsTheHandshakeAfterTenSeconds(string kind)
    {
        using SendBearer bearer = new();
        ManualClock clock = new();
        if (kind == "node")
        {
            await using NodeClient client = NodeClient.CreateFromBearer(bearer);
            client.Replies.Clock = clock;
            client.ReplyTimeLimit = Timeout.InfiniteTimeSpan;
            await AssertHandshakeTimeoutAsync(bearer, clock, client, client.StartAsync(2, CancellationToken.None), client.IsPlexerHealthy, client.GetPlexerException);
        }
        else
        {
            await using PeerClient client = PeerClient.CreateFromBearer(bearer);
            client.Replies.Clock = clock;
            await AssertHandshakeTimeoutAsync(bearer, clock, client, client.StartAsync(42, TimeSpan.FromHours(1), CancellationToken.None), client.IsPlexerHealthy, client.GetPlexerException);
        }
    }

    private static async Task AssertStartCancelledAsync(
        SendBearer bearer,
        IAsyncDisposable client,
        Task start,
        CancellationTokenSource cancel,
        Func<bool> isHealthy)
    {
        try
        {
            Assert.False(start.IsCompleted);
            await cancel.CancelAsync();
            _ = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => start.WaitAsync(N5Wire.Deadline, CancellationToken.None));
            await client.DisposeAsync();
            Assert.True(bearer.IsDisposed);
            Assert.False(isHealthy());
        }
        finally
        {
            await client.DisposeAsync();
            _ = await Record.ExceptionAsync(() => start);
        }
    }

    [Theory]
    [InlineData("node")]
    [InlineData("peer")]
    public async Task StartIsCancelledByItsToken(string kind)
    {
        using SendBearer bearer = new();
        using CancellationTokenSource cancel = new();
        using SendBearer secondBearer = new();
        CancellationToken cancelled = new(canceled: true);
        if (kind == "node")
        {
            await using NodeClient client = NodeClient.CreateFromBearer(bearer);
            client.Replies.Clock = new ManualClock();
            await AssertStartCancelledAsync(bearer, client, client.StartAsync(2, cancel.Token), cancel, client.IsPlexerHealthy);

            await using NodeClient second = NodeClient.CreateFromBearer(secondBearer);
            second.Replies.Clock = new ManualClock();
            _ = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => second.StartAsync(2, cancelled));
            Assert.Empty(secondBearer.Output.Bytes);
        }
        else
        {
            await using PeerClient client = PeerClient.CreateFromBearer(bearer);
            client.Replies.Clock = new ManualClock();
            await AssertStartCancelledAsync(bearer, client, client.StartAsync(42, TimeSpan.FromHours(1), cancel.Token), cancel, client.IsPlexerHealthy);

            await using PeerClient second = PeerClient.CreateFromBearer(secondBearer);
            second.Replies.Clock = new ManualClock();
            _ = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => second.StartAsync(42, TimeSpan.FromHours(1), cancelled));
            Assert.Empty(secondBearer.Output.Bytes);
        }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task NodeDisposeAsyncReturnsOnceTheBearerIsDisposed(bool started)
    {
        using SendBearer bearer = new();
        using CancellationTokenSource startup = new();
        await using NodeClient client = started
            ? await N5ReplyLimitTests.StartedNodeAsync(bearer, new ManualClock(), startup.Token)
            : NodeClient.CreateFromBearer(bearer);
        if (started)
        {
            await startup.CancelAsync();          // cancelling the start token after the start changes nothing
            Assert.True(client.IsPlexerHealthy());
        }

        await DisposeWaitsForTheBearerAsync(bearer, client, client.ChainSync);

        Assert.False(client.IsPlexerHealthy());
        await client.DisposeAsync();
        Assert.Null(Record.Exception(client.Dispose));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task PeerDisposeAsyncReturnsOnceTheBearerIsDisposed(bool started)
    {
        using SendBearer bearer = new();
        using CancellationTokenSource startup = new();
        await using PeerClient client = started
            ? await StartedPeerAsync(bearer, new ManualClock(), startup.Token)
            : PeerClient.CreateFromBearer(bearer);
        if (started)
        {
            await startup.CancelAsync();
            Assert.Equal(14, client.NegotiatedVersion?.Value);
            Assert.True(client.IsPlexerHealthy());
            Assert.True(client.IsKeepAliveHealthy());
        }
        else
        {
            Assert.Null(client.NegotiatedVersion);
        }

        await DisposeWaitsForTheBearerAsync(bearer, client, client.ChainSync);

        Assert.False(client.IsPlexerHealthy());
        Assert.False(client.IsKeepAliveHealthy());
        await client.DisposeAsync();
        Assert.Null(Record.Exception(client.Dispose));
    }

    [Fact]
    public async Task ASecondPeerStartThrowsAndLeavesTheConnection()
    {
        using SendBearer bearer = new();
        await using PeerClient client = await StartedPeerAsync(bearer, new ManualClock(), CancellationToken.None);
        int sent = bearer.Output.Bytes.Length;

        Task second = client.StartAsync(42, TimeSpan.FromHours(1), CancellationToken.None);

        Assert.True(second.IsFaulted);                      // refused before anything is sent or awaited
        _ = await Assert.ThrowsAsync<InvalidOperationException>(() => second);
        Assert.Equal(sent, bearer.Output.Bytes.Length);     // no second proposal
        Assert.True(client.IsPlexerHealthy());
        Assert.True(client.IsKeepAliveHealthy());
        Task tip = client.ChainSync.NextRequestAsync(CancellationToken.None);
        Assert.False(tip.IsCompleted);                      // the connection is open: on an ended one this call has already failed
        await client.DisposeAsync();
        _ = await Record.ExceptionAsync(() => tip);
    }

    [Fact]
    public async Task ASilentPeerEndsTheConnectionWhenAKeepAliveGoesUnanswered()
    {
        using SendBearer bearer = new();
        ManualClock clock = new();
        await using PeerClient client = await StartedPeerAsync(bearer, clock, CancellationToken.None);
        ManualTimer tick = clock.Timers[^1];                // the keep-alive loop's periodic timer
        Assert.Equal(TimeSpan.FromHours(1), tick.Due);
        Task tip = client.ChainSync.NextRequestAsync(CancellationToken.None);   // a caller waiting at the tip
        TaskCompletionSource sent = new(TaskCreationOptions.RunContinuationsAsynchronously);
        bearer.Output.Flush = () =>
        {
            _ = sent.TrySetResult();
            return default;
        };

        tick.Fire();
        await sent.Task.WaitAsync(N5Wire.Deadline, CancellationToken.None);    // the keep-alive request went out; its limit was started on the line before
        ManualTimer timer = clock.Timers[^1];
        Assert.Equal(TimeSpan.FromSeconds(60), timer.Due);
        timer.Fire();

        ConnectionClosedException closed = await Assert.ThrowsAsync<ConnectionClosedException>(() => tip.WaitAsync(N5Wire.Deadline, CancellationToken.None));
        ReplyTimeoutException timeout = Assert.IsType<ReplyTimeoutException>(closed.InnerException);
        Assert.Equal(ProtocolType.KeepAlive, timeout.Protocol);
        Assert.Equal(TimeSpan.FromSeconds(60), timeout.Limit);
        await client.DisposeAsync();
        Assert.False(client.IsKeepAliveHealthy());
        ConnectionClosedException keepAliveClosed = Assert.IsType<ConnectionClosedException>(client.GetKeepAliveException());
        Assert.Same(timeout, keepAliveClosed.InnerException);
        Assert.Same(timeout, client.GetPlexerException());
    }

    [Fact]
    public async Task ADisposeRightAfterTheHandshakeStopsTheKeepAlive()
    {
        using SendBearer bearer = new();
        ManualClock clock = new();
        using PeerClient client = PeerClient.CreateFromBearer(bearer);
        client.Replies.Clock = clock;

        Task start = client.StartAsync(42, TimeSpan.FromHours(1), CancellationToken.None);
        int proposalBytes = bearer.Output.Bytes.Length;
        ManualTimer handshakeTimer = Assert.Single(clock.Timers);
        handshakeTimer.OnDispose = client.Dispose;

        await N5Wire.ReplyAsync(bearer, ProtocolType.Handshake, Convert.FromHexString("83010e84182af500f4"));
        await start.WaitAsync(N5Wire.Deadline, CancellationToken.None);

        await client.DisposeAsync().AsTask().WaitAsync(N5Wire.Deadline, CancellationToken.None);
        Assert.False(client.IsKeepAliveHealthy());
        Assert.Equal(proposalBytes, bearer.Output.Bytes.Length);
    }

    [Theory]
    [InlineData("node", false)]
    [InlineData("node", true)]
    [InlineData("peer", false)]
    [InlineData("peer", true)]
    public async Task AConnectToAClosedPortFailsOrIsCancelled(string kind, bool cancelled)
    {
        int port = N5Wire.ClosedPort();
        CancellationToken token = cancelled ? new CancellationToken(canceled: true) : CancellationToken.None;
        async Task ConnectAsync()
        {
            if (kind == "node")
            {
                await using NodeClient client = await NodeClient.ConnectAsync("127.0.0.1", port, token);
            }
            else
            {
                await using PeerClient client = await PeerClient.ConnectAsync("127.0.0.1", port, token);
            }
        }

        if (cancelled)
        {
            _ = await Assert.ThrowsAnyAsync<OperationCanceledException>(ConnectAsync);
        }
        else
        {
            _ = await Assert.ThrowsAnyAsync<SocketException>(ConnectAsync);
        }
    }
}
