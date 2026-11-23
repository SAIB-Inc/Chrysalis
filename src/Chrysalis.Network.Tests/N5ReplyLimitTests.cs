using Chrysalis.Codec.Types;
using Chrysalis.Network.Cbor.Handshake;
using Chrysalis.Network.Cbor.LocalStateQuery;
using Chrysalis.Network.Cbor.LocalTxMonitor;
using Chrysalis.Network.Cbor.LocalTxSubmit;
using Chrysalis.Network.Core;
using Chrysalis.Network.Multiplexer;
using Xunit;

namespace Chrysalis.Network.Tests;

internal sealed class ManualClock : TimeProvider
{
    internal List<ManualTimer> Timers { get; } = [];

    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
    {
        ManualTimer timer = new(callback, state, dueTime);
        Timers.Add(timer);
        return timer;
    }
}

internal sealed class ManualTimer(TimerCallback callback, object? state, TimeSpan due) : ITimer
{
    internal TimeSpan Due { get; } = due;
    internal bool Disposed { get; private set; }
    internal Action? OnDispose { get; set; }
    internal void Fire() => callback(state);
    public bool Change(TimeSpan dueTime, TimeSpan period) => throw new NotSupportedException();
    public void Dispose()
    {
        Disposed = true;
        OnDispose?.Invoke();
    }
    public ValueTask DisposeAsync()
    {
        Disposed = true;
        OnDispose?.Invoke();
        return ValueTask.CompletedTask;
    }
}

public sealed class N5ReplyLimitTests
{
    private static readonly TimeSpan Limit = TimeSpan.FromSeconds(7);
    private static readonly byte[] QueryAcquired = PreprodFixtureTests.Wire("c1-in-p7-002.cbor");
    private static readonly byte[] MonitorAcquired = PreprodFixtureTests.Wire("c3-in-p9-002.cbor");
    private static readonly SubmitTx Transaction = LocalTxSubmissionMessages.SubmitTx(
        new EraTx(6, new CborEncodedValue(Convert.FromHexString("84a300d901028001800200a0f5f6"))));

    /// <summary>A NodeClient over the bearer, started against the accept of B1NodeClientApiTests.cs:24 (version 32788, magic 2).</summary>
    internal static async Task<NodeClient> StartedNodeAsync(SendBearer bearer, ManualClock clock, CancellationToken startup)
    {
        NodeClient client = NodeClient.CreateFromBearer(bearer);
        try
        {
            client.Replies.Clock = clock;
            Task start = client.StartAsync(2, startup);
            await N5Wire.ReplyAsync(bearer, ProtocolType.Handshake, Convert.FromHexString("83011980148202f4"));
            await start.WaitAsync(N5Wire.Deadline, CancellationToken.None);
            return client;
        }
        catch
        {
            await client.DisposeAsync();
            throw;
        }
    }

    [Theory]
    [InlineData("monitor acquire")]
    [InlineData("monitor next-tx")]
    [InlineData("monitor has-tx")]
    [InlineData("monitor sizes")]
    [InlineData("monitor measures")]
    [InlineData("submit")]
    [InlineData("query acquire")]
    [InlineData("query result")]
    [InlineData("query result with minimum")]
    [InlineData("chain-sync intersect")]
    public async Task ASilentNodeEndsTheConnectionWithAReplyTimeout(string call)
    {
        ArgumentNullException.ThrowIfNull(call);
        using SendBearer bearer = new();
        ManualClock clock = new();
        await using NodeClient client = await StartedNodeAsync(bearer, clock, CancellationToken.None);
        client.ReplyTimeLimit = Limit;
        if (call.StartsWith("query result", StringComparison.Ordinal))
        {
            Task acquire = client.LocalStateQuery.AcquireAsync(null, CancellationToken.None);
            await N5Wire.ReplyAsync(bearer, ProtocolType.LocalStateQuery, QueryAcquired);
            await acquire.WaitAsync(N5Wire.Deadline, CancellationToken.None);
        }
        Task pending = call switch
        {
            "monitor acquire" => client.LocalTxMonitor.AcquireAsync(CancellationToken.None),
            "monitor next-tx" => client.LocalTxMonitor.NextTxAsync(CancellationToken.None),
            "monitor has-tx" => client.LocalTxMonitor.HasTxAsync(new string('0', 64), CancellationToken.None),
            "monitor sizes" => client.LocalTxMonitor.GetSizesAsync(CancellationToken.None),
            "monitor measures" => client.LocalTxMonitor.GetMeasuresAsync(CancellationToken.None),
            "submit" => client.LocalTxSubmit.SubmitTxAsync(Transaction, CancellationToken.None),
            "query acquire" => client.LocalStateQuery.AcquireAsync(null, CancellationToken.None),
            "query result" => client.LocalStateQuery.QueryAsync(RawQueries.GetCurrentEra, CancellationToken.None),
            "query result with minimum" => client.LocalStateQuery.QueryAsync(RawQueries.GetCurrentEra, new N2CVersion(32784), CancellationToken.None),
            "chain-sync intersect" => client.ChainSync.FindIntersectionAsync([], CancellationToken.None),
            _ => throw new ArgumentOutOfRangeException(nameof(call))
        };
        ProtocolType protocol = call.Split(' ')[0] switch
        {
            "monitor" => ProtocolType.LocalTxMonitor,
            "submit" => ProtocolType.LocalTxSubmission,
            "chain-sync" => ProtocolType.ClientChainSync,
            _ => ProtocolType.LocalStateQuery
        };
        ManualTimer timer = clock.Timers[^1];
        Assert.Equal(Limit, timer.Due);
        Assert.False(pending.IsCompleted);

        timer.Fire();

        ConnectionClosedException closed = await Assert.ThrowsAsync<ConnectionClosedException>(() => pending.WaitAsync(N5Wire.Deadline, CancellationToken.None));
        ReplyTimeoutException timeout = Assert.IsType<ReplyTimeoutException>(closed.InnerException);
        Assert.Equal(protocol, timeout.Protocol);
        Assert.Equal(Limit, timeout.Limit);
        ConnectionClosedException later = await Assert.ThrowsAsync<ConnectionClosedException>(
            () => client.LocalTxSubmit.SubmitTxAsync(Transaction, CancellationToken.None));
        Assert.Same(timeout, later.InnerException);
        if (call == "chain-sync intersect")
        {
            _ = await Assert.ThrowsAsync<InvalidOperationException>(() => client.ChainSync.NextRequestAsync(CancellationToken.None));
        }
        await client.DisposeAsync();
        Assert.False(client.IsPlexerHealthy());
        Assert.Same(timeout, client.GetPlexerException());
        Assert.True(client.Completion.IsFaulted);
        Assert.Same(timeout, Assert.IsType<ConnectionClosedException>(client.Completion.Exception!.InnerExceptions[0]).InnerException);
    }

    [Fact]
    public async Task AReplyInTimeDisarmsTheLimit()
    {
        using SendBearer bearer = new();
        ManualClock clock = new();
        await using NodeClient client = await StartedNodeAsync(bearer, clock, CancellationToken.None);

        Task<LocalTxMonitorMessage> acquire = client.LocalTxMonitor.AcquireAsync(CancellationToken.None);
        ManualTimer timer = clock.Timers[^1];
        await N5Wire.ReplyAsync(bearer, ProtocolType.LocalTxMonitor, MonitorAcquired);
        _ = Assert.IsType<Acquired>(await acquire.WaitAsync(N5Wire.Deadline, CancellationToken.None));
        Assert.True(timer.Disposed);

        timer.Fire();
        Task sizes = client.LocalTxMonitor.GetSizesAsync(CancellationToken.None);
        Assert.False(sizes.IsCompleted);
        await client.DisposeAsync();
        _ = await Record.ExceptionAsync(() => sizes);
    }

    [Fact]
    public async Task ACallCancelledByItsTokenDisarmsTheLimitAndKeepsTheConnection()
    {
        using SendBearer bearer = new();
        ManualClock clock = new();
        await using NodeClient client = await StartedNodeAsync(bearer, clock, CancellationToken.None);
        using CancellationTokenSource cancel = new();

        Task sizes = client.LocalTxMonitor.GetSizesAsync(cancel.Token);
        ManualTimer timer = clock.Timers[^1];
        Assert.False(sizes.IsCompleted);
        await cancel.CancelAsync();

        _ = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => sizes.WaitAsync(N5Wire.Deadline, CancellationToken.None));
        Assert.True(timer.Disposed);

        timer.Fire();
        Assert.True(client.IsPlexerHealthy());
        Task next = client.LocalTxMonitor.NextTxAsync(CancellationToken.None);
        Assert.False(next.IsCompleted);
        await client.DisposeAsync();
        _ = await Record.ExceptionAsync(() => next);
    }

    [Fact]
    public async Task AReplyJustInsideTheDefaultLimitIsNotCut()
    {
        using SendBearer bearer = new();
        ManualClock clock = new();
        await using NodeClient client = await StartedNodeAsync(bearer, clock, CancellationToken.None);

        Task acquire = client.LocalStateQuery.AcquireAsync(null, CancellationToken.None);
        ManualTimer timer = clock.Timers[^1];
        Assert.Equal(TimeSpan.FromSeconds(180), timer.Due);
        Assert.False(acquire.IsCompleted);

        await N5Wire.ReplyAsync(bearer, ProtocolType.LocalStateQuery, QueryAcquired);
        await acquire.WaitAsync(N5Wire.Deadline, CancellationToken.None);
        Assert.True(client.LocalStateQuery.IsAcquired);
        Assert.True(timer.Disposed);

        timer.Fire();
        Assert.True(client.IsPlexerHealthy());
        Task next = client.LocalStateQuery.QueryAsync(RawQueries.GetCurrentEra, CancellationToken.None);
        Assert.False(next.IsCompleted);
        await client.DisposeAsync();
        _ = await Record.ExceptionAsync(() => next);
    }

    [Fact]
    public async Task AnInfiniteLimitStartsNoTimer()
    {
        using SendBearer bearer = new();
        ManualClock clock = new();
        await using NodeClient client = await StartedNodeAsync(bearer, clock, CancellationToken.None);
        int before = clock.Timers.Count;
        client.ReplyTimeLimit = Timeout.InfiniteTimeSpan;

        Task pending = client.LocalTxMonitor.NextTxAsync(CancellationToken.None);
        Assert.Equal(before, clock.Timers.Count);
        Assert.False(pending.IsCompleted);
        await client.DisposeAsync();
        _ = await Record.ExceptionAsync(() => pending);
    }

    [Fact]
    public async Task ReplyTimeLimitDefaultsTo180SecondsAndRejectsOtherNonPositiveValues()
    {
        using SendBearer bearer = new();
        await using NodeClient client = NodeClient.CreateFromBearer(bearer);
        TimeSpan longest = TimeSpan.FromMilliseconds(uint.MaxValue - 1);
        Assert.Equal(TimeSpan.FromSeconds(180), client.ReplyTimeLimit);
        _ = Assert.Throws<ArgumentOutOfRangeException>(() => client.ReplyTimeLimit = TimeSpan.Zero);
        _ = Assert.Throws<ArgumentOutOfRangeException>(() => client.ReplyTimeLimit = TimeSpan.FromMilliseconds(-2));
        _ = Assert.Throws<ArgumentOutOfRangeException>(() => client.ReplyTimeLimit = longest + TimeSpan.FromMilliseconds(1));
        _ = Assert.Throws<ArgumentOutOfRangeException>(() => client.ReplyTimeLimit = TimeSpan.FromDays(50));
        client.ReplyTimeLimit = longest;
        Assert.Equal(longest, client.ReplyTimeLimit);
        client.ReplyTimeLimit = Timeout.InfiniteTimeSpan;
        Assert.Equal(Timeout.InfiniteTimeSpan, client.ReplyTimeLimit);
        client.ReplyTimeLimit = TimeSpan.FromSeconds(5);
        Assert.Equal(TimeSpan.FromSeconds(5), client.ReplyTimeLimit);
    }

    [Fact]
    public async Task AnAcquireWhileASnapshotIsHeldHasNoLimit()
    {
        using SendBearer bearer = new();
        ManualClock clock = new();
        await using NodeClient client = await StartedNodeAsync(bearer, clock, CancellationToken.None);

        int beforeFirst = clock.Timers.Count;
        Task<LocalTxMonitorMessage> first = client.LocalTxMonitor.AcquireAsync(CancellationToken.None);
        Assert.Equal(beforeFirst + 1, clock.Timers.Count);
        await N5Wire.ReplyAsync(bearer, ProtocolType.LocalTxMonitor, MonitorAcquired);
        _ = Assert.IsType<Acquired>(await first.WaitAsync(N5Wire.Deadline, CancellationToken.None));

        int beforeSecond = clock.Timers.Count;
        Task<LocalTxMonitorMessage> second = client.LocalTxMonitor.AcquireAsync(CancellationToken.None);
        Assert.Equal(beforeSecond, clock.Timers.Count);
        Assert.False(second.IsCompleted);
        await N5Wire.ReplyAsync(bearer, ProtocolType.LocalTxMonitor, MonitorAcquired);
        _ = Assert.IsType<Acquired>(await second.WaitAsync(N5Wire.Deadline, CancellationToken.None));

        await client.LocalTxMonitor.ReleaseAsync(CancellationToken.None);

        int beforeThird = clock.Timers.Count;
        Task<LocalTxMonitorMessage> third = client.LocalTxMonitor.AcquireAsync(CancellationToken.None);
        Assert.Equal(beforeThird + 1, clock.Timers.Count);
        await N5Wire.ReplyAsync(bearer, ProtocolType.LocalTxMonitor, MonitorAcquired);
        _ = Assert.IsType<Acquired>(await third.WaitAsync(N5Wire.Deadline, CancellationToken.None));
    }
}
