using Chrysalis.Network.Core;
using Chrysalis.Network.Cbor.ChainSync;
using Chrysalis.Network.Multiplexer;
using Xunit;
using Monitor = Chrysalis.Network.Cbor.LocalTxMonitor;
using State = Chrysalis.Network.Cbor.LocalStateQuery.Messages;

namespace Chrysalis.Network.Tests;

public class PreprodChannelReplayTests
{
    [Fact]
    public async Task ThreeCapturedSegmentsReassembleOneRollForward()
    {
        using CancellationTokenSource deadline = new(TimeSpan.FromSeconds(10));
        using ProtocolReplay replay = new(ProtocolType.ClientChainSync,
            PreprodFixtures.Read("wire/c7-in-p5-004.cbor"),
            PreprodFixtures.Read("wire/c7-in-p5-005.cbor"),
            PreprodFixtures.Read("wire/c7-in-p5-006.cbor"));
        ChannelBuffer buffer = new(replay.Channel);
        N2CMessageRollForward forward = await buffer.ReceiveFullMessageAsync<N2CMessageRollForward>(deadline.Token);
        Assert.Equal(2, forward.Idx);
        Assert.Equal(PreprodFixtures.Read("blocks/block-139039741.cbor"), forward.Payload.Value.ToArray());
    }

    [Fact]
    public async Task CoalescedQueryReleaseAndDoneAreTwoMessages()
    {
        using CancellationTokenSource deadline = new(TimeSpan.FromSeconds(10));
        using ProtocolReplay replay = new(ProtocolType.LocalStateQuery, PreprodFixtures.Read("wire/c4-out-p7-004.cbor"));
        ChannelBuffer buffer = new(replay.Channel);
        State.Release release = await buffer.ReceiveFullMessageAsync<State.Release>(deadline.Token);
        State.IsMessageDone done = await buffer.ReceiveFullMessageAsync<State.IsMessageDone>(deadline.Token);
        Assert.Equal(5, release.Idx);
        Assert.Equal(7, done.Idx);
    }

    [Fact]
    public async Task CoalescedMonitorReleaseAndDoneAreTwoMessages()
    {
        using CancellationTokenSource deadline = new(TimeSpan.FromSeconds(10));
        using ProtocolReplay replay = new(ProtocolType.LocalTxMonitor, PreprodFixtures.Read("wire/c5-out-p9-004.cbor"));
        ChannelBuffer buffer = new(replay.Channel);
        Monitor.Release release = await buffer.ReceiveFullMessageAsync<Monitor.Release>(deadline.Token);
        Monitor.Done done = await buffer.ReceiveFullMessageAsync<Monitor.Done>(deadline.Token);
        Assert.Equal(3, release.Idx);
        Assert.Equal(0, done.Idx);
    }
}
