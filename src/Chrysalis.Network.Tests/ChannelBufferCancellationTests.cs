using System.IO.Pipelines;
using System.Threading.Channels;
using Chrysalis.Codec.Types;
using Chrysalis.Network.Cbor.BlockFetch;
using Chrysalis.Network.Cbor.LocalStateQuery.Messages;
using Chrysalis.Network.Core;
using Chrysalis.Network.MiniProtocols;
using Chrysalis.Network.Multiplexer;
using Xunit;

namespace Chrysalis.Network.Tests;

public sealed class ChannelBufferCancellationTests
{
    [Fact]
    public async Task SendFullMessageAsyncWithCancelledTokenThrowsAndWritesNothing()
    {
        using ProtocolReplay replay = new(ProtocolType.LocalStateQuery);
        ChannelBuffer buffer = new(replay.Channel);
        CancellationToken cancelled = new(canceled: true);

        OperationCanceledException error = await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => buffer.SendFullMessageAsync(new Release(5), cancelled));
        Assert.Equal(cancelled, error.CancellationToken);
        replay.AssertNothingSent();
    }

    [Fact]
    public async Task SendFullMessageAsyncCancelledBetweenSegmentsThrowsAndStops()
    {
        Pipe outgoing = new(new PipeOptions(pauseWriterThreshold: 1, resumeWriterThreshold: 1));
        Channel<ReadOnlyMemory<byte>> incoming = Channel.CreateUnbounded<ReadOnlyMemory<byte>>();
        using SemaphoreSlim writeLock = new(1, 1);
        AgentChannel channel = new(ProtocolType.BlockFetch, outgoing.Writer, incoming.Reader, writeLock);
        ChannelBuffer buffer = new(channel);
        BlockBody message = new(4, new CborEncodedValue(new byte[ProtocolConstants.MaxSegmentPayloadLength + 1]));
        using CancellationTokenSource cancel = new();
        using CancellationTokenSource deadline = new(TimeSpan.FromSeconds(5));

        Task send = buffer.SendFullMessageAsync(message, cancel.Token);
        try
        {
            const int expectedFirstFrame = ProtocolConstants.SegmentHeaderSize + ProtocolConstants.MaxSegmentPayloadLength;
            ReadResult firstChunk = await outgoing.Reader.ReadAtLeastAsync(expectedFirstFrame, deadline.Token);
            Assert.Equal(expectedFirstFrame, firstChunk.Buffer.Length);
            Assert.False(send.IsCompleted);

            await cancel.CancelAsync();
            outgoing.Reader.AdvanceTo(firstChunk.Buffer.End);

            OperationCanceledException error = await Assert.ThrowsAnyAsync<OperationCanceledException>(
                () => send.WaitAsync(deadline.Token));
            Assert.Equal(cancel.Token, error.CancellationToken);

            ProtocolReplay.AssertNothingSent(outgoing, "A cancelled multi-segment send wrote a second segment.");
        }
        finally
        {
            await cancel.CancelAsync();
            await outgoing.Reader.CompleteAsync();
            await outgoing.Writer.CompleteAsync();
            _ = incoming.Writer.TryComplete();
            _ = await Record.ExceptionAsync(() => send.WaitAsync(TimeSpan.FromSeconds(5)));
        }
    }

    [Fact]
    public async Task LocalStateQueryReleaseAsyncWithCancelledTokenThrowsAndKeepsIsAcquired()
    {
        using ProtocolReplay replay = new(ProtocolType.LocalStateQuery, PreprodFixtureTests.Wire("c1-in-p7-002.cbor"));
        await using LocalStateQuery query = new(replay.Channel);
        using CancellationTokenSource deadline = new(TimeSpan.FromSeconds(5));
        await query.AcquireAsync(null, deadline.Token);
        Assert.True(query.IsAcquired);
        _ = replay.ReadSentPayload();

        CancellationToken cancelled = new(canceled: true);
        OperationCanceledException releaseError = await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => query.ReleaseAsync(cancelled));
        Assert.Equal(cancelled, releaseError.CancellationToken);
        Assert.True(query.IsAcquired);
        replay.AssertNothingSent();

        OperationCanceledException acquireError = await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => query.AcquireAsync(null, cancelled));
        Assert.Equal(cancelled, acquireError.CancellationToken);
        Assert.True(query.IsAcquired);
        replay.AssertNothingSent();
    }
}
