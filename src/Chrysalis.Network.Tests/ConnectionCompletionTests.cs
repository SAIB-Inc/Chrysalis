using System.Buffers;
using System.Reflection;
using Chrysalis.Codec.Types;
using Chrysalis.Network.Cbor.BlockFetch;
using Chrysalis.Network.Cbor.Handshake;
using Chrysalis.Network.Core;
using Chrysalis.Network.Multiplexer;
using Chrysalis.Network.Tests.Receive;
using SAIB.Cbor;
using Xunit;
using ProtocolType = Chrysalis.Network.Core.ProtocolType;

namespace Chrysalis.Network.Tests;

public sealed class ConnectionCompletionTests
{
    private static readonly FieldInfo NodePlexerField =
        typeof(NodeClient).GetField("_plexer", BindingFlags.Instance | BindingFlags.NonPublic)!;
    private static readonly FieldInfo NodePlexerTaskField =
        typeof(NodeClient).GetField("_plexerTask", BindingFlags.Instance | BindingFlags.NonPublic)!;
    private static readonly FieldInfo PeerPlexerField =
        typeof(PeerClient).GetField("_plexer", BindingFlags.Instance | BindingFlags.NonPublic)!;
    private static readonly FieldInfo PeerPlexerTaskField =
        typeof(PeerClient).GetField("_plexerTask", BindingFlags.Instance | BindingFlags.NonPublic)!;
    private static readonly FieldInfo PlexerConnectionField =
        typeof(Plexer).GetField("_connection", BindingFlags.Instance | BindingFlags.NonPublic)!;
    private static readonly FieldInfo KeepAliveBufferField =
        typeof(MiniProtocols.KeepAliveClient).GetField("_buffer", BindingFlags.Instance | BindingFlags.NonPublic)!;
    private static readonly FieldInfo ChannelBufferChannelField =
        typeof(ChannelBuffer).GetFields(BindingFlags.Instance | BindingFlags.NonPublic).Single(field => field.FieldType == typeof(AgentChannel));

    [Theory]
    [InlineData("plexer")]
    [InlineData("node")]
    [InlineData("peer")]
    public async Task BeforeStartAndWhileRunningCompletionIsPending(string kind)
    {
        using SendBearer bearer = new();
        ManualClock clock = new();
        if (kind == "plexer")
        {
            using Plexer plexer = new(bearer);
            Assert.False(plexer.Completion.IsCompleted);
            Assert.False(plexer.IsPlexerHealthy());
            Assert.Null(plexer.GetPlexerException());

            Task run = plexer.RunAsync(CancellationToken.None);
            try
            {
                Assert.False(plexer.Completion.IsCompleted);
                Assert.True(plexer.IsPlexerHealthy());
                Assert.Null(plexer.GetPlexerException());
            }
            finally
            {
                plexer.Dispose();
                await run.WaitAsync(N5Wire.Deadline, CancellationToken.None);
            }
        }
        else if (kind == "node")
        {
            await using NodeClient client = NodeClient.CreateFromBearer(bearer);
            client.Replies.Clock = clock;
            await AssertClientPendingAsync(bearer, (Plexer)NodePlexerField.GetValue(client)!, client.Completion, client.IsPlexerHealthy, client.GetPlexerException,
                () => client.StartAsync(2, CancellationToken.None), "83011980148202f4");
        }
        else
        {
            await using PeerClient client = PeerClient.CreateFromBearer(bearer);
            client.Replies.Clock = clock;
            await AssertClientPendingAsync(bearer, (Plexer)PeerPlexerField.GetValue(client)!, client.Completion, client.IsPlexerHealthy, client.GetPlexerException,
                () => client.StartAsync(42, TimeSpan.FromHours(1), CancellationToken.None), "83010e84182af500f4", client.IsKeepAliveHealthy, client.GetKeepAliveException);
        }
    }

    [Theory]
    [InlineData("clean-eof")]
    [InlineData("partial-eof")]
    [InlineData("partial-message-eof")]
    [InlineData("send-io")]
    [InlineData("invalid-cbor")]
    [InlineData("over-cap")]
    [InlineData("mid-message-cancel")]
    public async Task EveryFaultRowSetsCompletionRunAsyncAndGettersToTheSameCause(string kind)
    {
        if (kind == "over-cap")
        {
            using ScriptedReceiveBearer bearer = new();
            using Plexer plexer = new(bearer);
            AgentChannel channel = plexer.SubscribeClient(ProtocolType.BlockFetch);
            byte[] message = new byte[2_500_001];
            Convert.FromHexString("5A0026259C").CopyTo(message, 0);
            bearer.Deliver(OwnedReceiveTests.Frames(ProtocolType.BlockFetch, message).Wire.ToArray());
            Task run = plexer.RunAsync(CancellationToken.None);
            try
            {
                ConnectionClosedException runClosed = await Assert.ThrowsAsync<ConnectionClosedException>(() => run.WaitAsync(N5Wire.Deadline, CancellationToken.None));
                InvalidDataException cause = Assert.IsType<InvalidDataException>(runClosed.InnerException);
                await AssertFaultSurvivesDisposeAsync(plexer, channel, runClosed, cause);
            }
            finally
            {
                plexer.Dispose();
                _ = await Record.ExceptionAsync(() => run.WaitAsync(N5Wire.Deadline, CancellationToken.None));
            }
            return;
        }

        using SendBearer sendBearer = new();
        using Plexer sendPlexer = new(sendBearer);
        AgentChannel sendChannel = sendPlexer.SubscribeClient(ProtocolType.BlockFetch);
        ChannelBuffer buffer = new(sendChannel);
        Task sendRun = sendPlexer.RunAsync(CancellationToken.None);
        try
        {
            if (kind == "clean-eof")
            {
                await N5Wire.ReplyAsync(sendBearer, ProtocolType.BlockFetch, [0x81, 0x05]);
                await sendBearer.Input.CompleteAsync();
                ConnectionClosedException runClosed = await Assert.ThrowsAsync<ConnectionClosedException>(() => sendRun.WaitAsync(N5Wire.Deadline, CancellationToken.None));
                Assert.Null(runClosed.InnerException);
                _ = Assert.IsType<BatchDone>(await buffer.ReceiveFullMessageAsync<BlockFetchMessage>(CancellationToken.None));
                ConnectionClosedException afterDrain = await Assert.ThrowsAsync<ConnectionClosedException>(async () => _ = await buffer.ReceiveFullMessageAsync<BlockFetchMessage>(CancellationToken.None));
                Assert.Null(afterDrain.InnerException);
                ConnectionClosedException completionClosed = await Assert.ThrowsAsync<ConnectionClosedException>(() => sendPlexer.Completion.WaitAsync(N5Wire.Deadline, CancellationToken.None));
                Assert.Same(completionClosed, runClosed);
                Assert.Same(completionClosed, sendPlexer.GetPlexerException());
                Assert.False(sendPlexer.IsPlexerHealthy());
                sendPlexer.Dispose();
                Assert.True(sendPlexer.Completion.IsFaulted);
                Assert.Same(completionClosed, sendPlexer.GetPlexerException());
            }
            else if (kind == "partial-eof")
            {
                _ = await sendBearer.Input.WriteAsync(new byte[] { 0, 0, 0, 0 }, CancellationToken.None);
                await sendBearer.Input.CompleteAsync();
                ConnectionClosedException runClosed = await Assert.ThrowsAsync<ConnectionClosedException>(() => sendRun.WaitAsync(N5Wire.Deadline, CancellationToken.None));
                EndOfStreamException cause = Assert.IsType<EndOfStreamException>(runClosed.InnerException);
                await AssertFaultSurvivesDisposeAsync(sendPlexer, sendChannel, runClosed, cause);
            }
            else if (kind == "partial-message-eof")
            {
                await N5Wire.ReplyAsync(sendBearer, ProtocolType.BlockFetch, [0x82, 0x04]);
                await sendBearer.Input.CompleteAsync();
                ConnectionClosedException runClosed = await Assert.ThrowsAsync<ConnectionClosedException>(() => sendRun.WaitAsync(N5Wire.Deadline, CancellationToken.None));
                EndOfStreamException cause = Assert.IsType<EndOfStreamException>(runClosed.InnerException);
                await AssertFaultSurvivesDisposeAsync(sendPlexer, sendChannel, runClosed, cause);
            }
            else if (kind == "send-io")
            {
                IOException injected = new("scripted send fault");
                sendBearer.Output.Flush = () => throw injected;
                ConnectionClosedException sendClosed = await Assert.ThrowsAsync<ConnectionClosedException>(() => sendChannel.EnqueueChunkAsync(new ReadOnlySequence<byte>([1]), CancellationToken.None));
                Assert.Same(injected, sendClosed.InnerException);
                ConnectionClosedException runClosed = await Assert.ThrowsAsync<ConnectionClosedException>(() => sendRun.WaitAsync(N5Wire.Deadline, CancellationToken.None));
                Assert.Same(injected, runClosed.InnerException);
                await AssertFaultSurvivesDisposeAsync(sendPlexer, sendChannel, runClosed, injected);
            }
            else if (kind == "invalid-cbor")
            {
                await N5Wire.ReplyAsync(sendBearer, ProtocolType.BlockFetch, [0xff]);
                ConnectionClosedException runClosed = await Assert.ThrowsAsync<ConnectionClosedException>(() => sendRun.WaitAsync(N5Wire.Deadline, CancellationToken.None));
                CborException cause = Assert.IsType<CborException>(runClosed.InnerException);
                await AssertFaultSurvivesDisposeAsync(sendPlexer, sendChannel, runClosed, cause);
            }
            else
            {
                using CancellationTokenSource cancel = new();
                BlockBody largeMessage = new(4, new CborEncodedValue(new byte[ProtocolConstants.MaxSegmentPayloadLength + 16]));
                sendBearer.Output.Flush = () =>
                {
                    cancel.Cancel();
                    return default;
                };
                OperationCanceledException sendCanceled = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => buffer.SendFullMessageAsync<BlockFetchMessage>(largeMessage, cancel.Token));
                ConnectionClosedException runClosed = await Assert.ThrowsAsync<ConnectionClosedException>(() => sendRun.WaitAsync(N5Wire.Deadline, CancellationToken.None));
                IOException cause = Assert.IsType<IOException>(runClosed.InnerException);
                Assert.Same(sendCanceled, cause.InnerException);
                await AssertFaultSurvivesDisposeAsync(sendPlexer, sendChannel, runClosed, cause);
            }
        }
        finally
        {
            sendPlexer.Dispose();
            _ = await Record.ExceptionAsync(() => sendRun.WaitAsync(N5Wire.Deadline, CancellationToken.None));
        }
    }

    [Theory]
    [InlineData("run-cancel")]
    [InlineData("node-dispose")]
    [InlineData("peer-dispose")]
    [InlineData("node-failed-start-refusal")]
    [InlineData("peer-failed-start-refusal")]
    [InlineData("node-dispose-before-start")]
    [InlineData("peer-dispose-before-start")]
    public async Task LocalEndCompletesCompletionWithNullException(string kind)
    {
        using SendBearer bearer = new();
        ManualClock clock = new();
        if (kind == "run-cancel")
        {
            using Plexer plexer = new(bearer);
            AgentChannel channel = plexer.SubscribeClient(ProtocolType.KeepAlive);
            using CancellationTokenSource stop = new();
            Task run = plexer.RunAsync(stop.Token);
            await stop.CancelAsync();
            OperationCanceledException runCanceled = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => run.WaitAsync(N5Wire.Deadline, CancellationToken.None));
            Assert.Equal(stop.Token, runCanceled.CancellationToken);
            await plexer.Completion.WaitAsync(N5Wire.Deadline, CancellationToken.None);
            Assert.Equal(TaskStatus.RanToCompletion, plexer.Completion.Status);
            Assert.False(plexer.IsPlexerHealthy());
            Assert.Null(plexer.GetPlexerException());
            ConnectionClosedException later = await Assert.ThrowsAsync<ConnectionClosedException>(() => channel.EnqueueChunkAsync(new ReadOnlySequence<byte>([1]), CancellationToken.None));
            _ = Assert.IsAssignableFrom<OperationCanceledException>(later.InnerException);
        }
        else if (kind == "node-dispose")
        {
            await using NodeClient client = await N5ReplyLimitTests.StartedNodeAsync(bearer, clock, CancellationToken.None);
            await client.DisposeAsync();
            await AssertLocalEndAsync(client.Completion, client.IsPlexerHealthy, client.GetPlexerException, client.ChainSync);
        }
        else if (kind == "peer-dispose")
        {
            await using PeerClient client = await N5LifecycleTests.StartedPeerAsync(bearer, clock, CancellationToken.None);
            await client.DisposeAsync();
            await AssertLocalEndAsync(client.Completion, client.IsPlexerHealthy, client.GetPlexerException, client.ChainSync, client.IsKeepAliveHealthy, client.GetKeepAliveException);
        }
        else if (kind == "node-failed-start-refusal")
        {
            await using NodeClient client = NodeClient.CreateFromBearer(bearer);
            client.Replies.Clock = clock;
            Task start = client.StartAsync(2, CancellationToken.None);
            await N5Wire.ReplyAsync(bearer, ProtocolType.Handshake, Convert.FromHexString("8202830219801463626164"));
            _ = await Assert.ThrowsAsync<HandshakeRefusedException>(() => start.WaitAsync(N5Wire.Deadline, CancellationToken.None));
            await AssertLocalEndAsync(client.Completion, client.IsPlexerHealthy, client.GetPlexerException, client.ChainSync);
        }
        else if (kind == "peer-failed-start-refusal")
        {
            await using PeerClient client = PeerClient.CreateFromBearer(bearer);
            client.Replies.Clock = clock;
            Task start = client.StartAsync(42, TimeSpan.FromHours(1), CancellationToken.None);
            await N5Wire.ReplyAsync(bearer, ProtocolType.Handshake, Convert.FromHexString("8202830219801463626164"));
            _ = await Assert.ThrowsAsync<HandshakeRefusedException>(() => start.WaitAsync(N5Wire.Deadline, CancellationToken.None));
            await AssertLocalEndAsync(client.Completion, client.IsPlexerHealthy, client.GetPlexerException, client.ChainSync, client.IsKeepAliveHealthy, client.GetKeepAliveException);
        }
        else if (kind == "node-dispose-before-start")
        {
            await using NodeClient client = NodeClient.CreateFromBearer(bearer);
            await client.DisposeAsync();
            await AssertLocalEndAsync(client.Completion, client.IsPlexerHealthy, client.GetPlexerException, client.ChainSync);
        }
        else
        {
            await using PeerClient client = PeerClient.CreateFromBearer(bearer);
            await client.DisposeAsync();
            await AssertLocalEndAsync(client.Completion, client.IsPlexerHealthy, client.GetPlexerException, client.ChainSync, client.IsKeepAliveHealthy, client.GetKeepAliveException);
        }
    }

    [Theory]
    [InlineData("idle-clean-eof")]
    [InlineData("inflight-io-fault")]
    [InlineData("late-mismatch")]
    public async Task KeepAliveEndsQuietlyOnExternalCloseAndLateMismatchCannotOverwriteFirstCause(string kind)
    {
        using SendBearer bearer = new();
        ManualClock clock = new();
        await using PeerClient client = await N5LifecycleTests.StartedPeerAsync(bearer, clock, CancellationToken.None);
        Task keepAliveTask = (Task)Recv1Mr4DefectTests.KeepAliveTaskField.GetValue(client)!;
        ManualTimer tick = clock.Timers[^1];

        if (kind == "idle-clean-eof")
        {
            await bearer.Input.CompleteAsync();
            _ = await Record.ExceptionAsync(() => client.Completion.WaitAsync(N5Wire.Deadline, CancellationToken.None));
            Assert.False(client.IsKeepAliveHealthy());
            tick.Fire();
            Assert.Null(await Record.ExceptionAsync(() => keepAliveTask.WaitAsync(N5Wire.Deadline, CancellationToken.None)));
            Assert.Null(client.GetKeepAliveException());
            ConnectionClosedException plexerClosed = Assert.IsType<ConnectionClosedException>(client.GetPlexerException());
            Assert.Null(plexerClosed.InnerException);
            return;
        }

        Plexer plexer = (Plexer)PeerPlexerField.GetValue(client)!;
        Connection connection = (Connection)PlexerConnectionField.GetValue(plexer)!;
        ProtocolQueue keepAliveQueue = GetKeepAliveQueue(client);
        byte[] badReply = await Recv1Mr4DefectTests.TriggerKeepAliveAndBuildWrongCookieReplyAsync(bearer, tick);
        await connection.WriteLock.WaitAsync(CancellationToken.None);
        _ = connection.WriteLock.Release();
        Assert.True(SpinWait.SpinUntil(() => keepAliveQueue.Waiting, N5Wire.Deadline));

        if (kind == "inflight-io-fault")
        {
            IOException ioe = new("inflight peer read fault");
            await bearer.Input.CompleteAsync(ioe);
            Assert.Null(await Record.ExceptionAsync(() => keepAliveTask.WaitAsync(N5Wire.Deadline, CancellationToken.None)));
            Assert.False(client.IsKeepAliveHealthy());
            Assert.Null(client.GetKeepAliveException());
            Assert.Same(ioe, client.GetPlexerException());
        }
        else
        {
            ReceiveSchedule wire = new("late-mismatch");
            wire.Segment(ProtocolType.KeepAlive, badReply);
            wire.Segment(ProtocolType.BlockFetch, [0xff]);
            _ = await bearer.Input.WriteAsync(wire.Wire.ToArray(), CancellationToken.None);

            _ = await Record.ExceptionAsync(() => keepAliveTask.WaitAsync(N5Wire.Deadline, CancellationToken.None));
            ConnectionClosedException completionClosed = await Assert.ThrowsAsync<ConnectionClosedException>(() => client.Completion.WaitAsync(N5Wire.Deadline, CancellationToken.None));
            CborException cborError = Assert.IsType<CborException>(completionClosed.InnerException);
            Assert.Same(cborError, client.GetPlexerException());
            _ = Assert.IsType<InvalidOperationException>(client.GetKeepAliveException());
            Assert.False(client.IsPlexerHealthy());
            Assert.False(client.IsKeepAliveHealthy());
        }
    }

    [Fact]
    public async Task SecondPlexerRunAsyncThrowsInvalidOperationException()
    {
        using SendBearer bearer = new();
        using Plexer plexer = new(bearer);
        Task first = plexer.RunAsync(CancellationToken.None);
        try
        {
            Task second = plexer.RunAsync(CancellationToken.None);
            Assert.True(second.IsFaulted);
            _ = await Assert.ThrowsAsync<InvalidOperationException>(() => second);
            Assert.False(first.IsCompleted);
            Assert.False(plexer.Completion.IsCompleted);
            Assert.True(plexer.IsPlexerHealthy());
            Assert.Null(plexer.GetPlexerException());
        }
        finally
        {
            plexer.Dispose();
            await first.WaitAsync(N5Wire.Deadline, CancellationToken.None);
        }
        Assert.Equal(TaskStatus.RanToCompletion, plexer.Completion.Status);
    }

    [Theory]
    [InlineData("node-read")]
    [InlineData("peer-read")]
    [InlineData("peer-keepalive")]
    public async Task DisposeAsyncWaitsForReadLoopAndKeepAliveLoop(string kind)
    {
        using SendBearer bearer = new();
        ManualClock clock = new();
        TaskCompletionSource gate = new(TaskCreationOptions.RunContinuationsAsynchronously);
        if (kind == "node-read")
        {
            await using NodeClient client = await N5ReplyLimitTests.StartedNodeAsync(bearer, clock, CancellationToken.None);
            Plexer plexer = (Plexer)NodePlexerField.GetValue(client)!;
            Task realRun = (Task)NodePlexerTaskField.GetValue(client)!;
            NodePlexerTaskField.SetValue(client, Task.WhenAll(realRun, gate.Task));
            await AssertDisposeWaitsForGateAsync(client, plexer, gate, realRun);
        }
        else
        {
            await using PeerClient client = await N5LifecycleTests.StartedPeerAsync(bearer, clock, CancellationToken.None);
            Plexer plexer = (Plexer)PeerPlexerField.GetValue(client)!;
            Task realRun = (Task)PeerPlexerTaskField.GetValue(client)!;
            Task realKeepAlive = (Task)Recv1Mr4DefectTests.KeepAliveTaskField.GetValue(client)!;
            FieldInfo targetField = kind == "peer-read" ? PeerPlexerTaskField : Recv1Mr4DefectTests.KeepAliveTaskField;
            Task targetTask = kind == "peer-read" ? realRun : realKeepAlive;
            targetField.SetValue(client, Task.WhenAll(targetTask, gate.Task));
            await AssertDisposeWaitsForGateAsync(client, plexer, gate, realRun, realKeepAlive);
        }
    }

    [Fact]
    public async Task ReplyLimitReleasesAStalledSend()
    {
        using SendBearer bearer = new();
        ManualClock clock = new();
        await using NodeClient client = await N5ReplyLimitTests.StartedNodeAsync(bearer, clock, CancellationToken.None);
        _ = DirectSendShutdownTests.ParkFlush(bearer.Output);
        Task pending = client.LocalTxMonitor.GetSizesAsync(CancellationToken.None);
        try
        {
            Assert.False(pending.IsCompleted);
            ManualTimer timer = clock.Timers[^1];
            Assert.Equal(TimeSpan.FromSeconds(180), timer.Due);
            Assert.False(timer.Disposed);

            timer.Fire();

            ConnectionClosedException closed = await Assert.ThrowsAsync<ConnectionClosedException>(() => pending.WaitAsync(N5Wire.Deadline, CancellationToken.None));
            ReplyTimeoutException timeout = Assert.IsType<ReplyTimeoutException>(closed.InnerException);
            Assert.Equal(ProtocolType.LocalTxMonitor, timeout.Protocol);
            Assert.Equal(TimeSpan.FromSeconds(180), timeout.Limit);
            Assert.True(client.Completion.IsFaulted);
            Assert.Same(timeout, Assert.IsType<ConnectionClosedException>(client.Completion.Exception!.InnerExceptions[0]).InnerException);
            Assert.Same(timeout, client.GetPlexerException());
            await client.DisposeAsync();
            Assert.True(bearer.IsDisposed);
        }
        finally
        {
            _ = await Record.ExceptionAsync(() => pending.WaitAsync(N5Wire.Deadline, CancellationToken.None));
        }
    }

    private static async Task AssertClientPendingAsync(SendBearer bearer, Plexer plexer, Task completion, Func<bool> isPlexerHealthy, Func<Exception?> getPlexerException,
        Func<Task> start, string acceptHex, Func<bool>? isKeepAliveHealthy = null, Func<Exception?>? getKeepAliveException = null)
    {
        Assert.Same(plexer.Completion, completion);
        Assert.False(completion.IsCompleted);
        Assert.False(isPlexerHealthy());
        Assert.Null(getPlexerException());
        if (isKeepAliveHealthy is not null) { Assert.False(isKeepAliveHealthy()); }
        if (getKeepAliveException is not null) { Assert.Null(getKeepAliveException()); }
        Task starting = start();
        await N5Wire.ReplyAsync(bearer, ProtocolType.Handshake, Convert.FromHexString(acceptHex));
        await starting.WaitAsync(N5Wire.Deadline, CancellationToken.None);
        Assert.False(completion.IsCompleted);
        Assert.True(isPlexerHealthy());
        Assert.Null(getPlexerException());
        if (isKeepAliveHealthy is not null) { Assert.True(isKeepAliveHealthy()); }
        if (getKeepAliveException is not null) { Assert.Null(getKeepAliveException()); }
    }

    private static async Task AssertLocalEndAsync(Task completion, Func<bool> isPlexerHealthy, Func<Exception?> getPlexerException, MiniProtocols.ChainSync chainSync,
        Func<bool>? isKeepAliveHealthy = null, Func<Exception?>? getKeepAliveException = null)
    {
        Assert.Equal(TaskStatus.RanToCompletion, completion.Status);
        Assert.False(isPlexerHealthy());
        Assert.Null(getPlexerException());
        if (isKeepAliveHealthy is not null) { Assert.False(isKeepAliveHealthy()); }
        if (getKeepAliveException is not null) { Assert.Null(getKeepAliveException()); }
        ConnectionClosedException later = await Assert.ThrowsAsync<ConnectionClosedException>(() => chainSync.DoneAsync(CancellationToken.None));
        _ = Assert.IsType<ObjectDisposedException>(later.InnerException);
    }

    private static async Task AssertDisposeWaitsForGateAsync(IDisposable client, Plexer plexer, TaskCompletionSource gate, params Task[] realTasks)
    {
        Assert.Null(Record.Exception(client.Dispose));
        await plexer.CloseAsync().WaitAsync(N5Wire.Deadline, CancellationToken.None);
        foreach (Task task in realTasks) { _ = await Record.ExceptionAsync(() => task.WaitAsync(N5Wire.Deadline, CancellationToken.None)); }
        ValueTask stop = ((IAsyncDisposable)client).DisposeAsync();
        try { Assert.False(stop.IsCompleted); }
        finally { _ = gate.TrySetResult(); }
        await stop.AsTask().WaitAsync(N5Wire.Deadline, CancellationToken.None);
    }

    private static async Task AssertFaultSurvivesDisposeAsync(Plexer plexer, AgentChannel channel, ConnectionClosedException runClosed, Exception expectedCause)
    {
        ConnectionClosedException completionClosed = await Assert.ThrowsAsync<ConnectionClosedException>(() => plexer.Completion.WaitAsync(N5Wire.Deadline, CancellationToken.None));
        Assert.Same(completionClosed, runClosed);
        Assert.Same(expectedCause, completionClosed.InnerException);
        Assert.Same(expectedCause, plexer.GetPlexerException());
        Assert.False(plexer.IsPlexerHealthy());
        ConnectionClosedException later = await Assert.ThrowsAsync<ConnectionClosedException>(() => channel.EnqueueChunkAsync(new ReadOnlySequence<byte>([1]), CancellationToken.None));
        Assert.Same(expectedCause, later.InnerException);
        plexer.Dispose();
        Assert.True(plexer.Completion.IsFaulted);
        Assert.Same(expectedCause, plexer.GetPlexerException());
    }

    private static ProtocolQueue GetKeepAliveQueue(PeerClient client)
    {
        ChannelBuffer buffer = (ChannelBuffer)KeepAliveBufferField.GetValue(client.KeepAlive)!;
        AgentChannel channel = (AgentChannel)ChannelBufferChannelField.GetValue(buffer)!;
        return Recv1Mr4DefectTests.GetQueue(channel);
    }
}
