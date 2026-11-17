using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using Chrysalis.Network.Multiplexer;
using System.Threading.Channels;
using Chrysalis.Network.Core;
using Xunit;
using Xunit.Abstractions;

namespace Chrysalis.Network.Tests.Receive;

[CollectionDefinition("Receive differential", DisableParallelization = true)]
public class ReceiveDifferentialIsolation;

[Collection("Receive differential")]
public sealed class ReceiveDifferentialTests(ITestOutputHelper output)
{
    [Fact]
    public void FrozenOracleMatchesThePinnedFc21a49Adaptations()
    {
        const string prefix = "Chrysalis.Network.Tests.Receive.Frozen.";
        using Stream metadata = typeof(ReceiveDifferentialTests).Assembly.GetManifestResourceStream(prefix + "SOURCE.json")
            ?? throw new InvalidOperationException("Missing frozen oracle metadata.");
        using JsonDocument source = JsonDocument.Parse(metadata);
        Assert.Equal("fc21a49", source.RootElement.GetProperty("commit").GetString());
        foreach (JsonProperty file in source.RootElement.GetProperty("frozen_sha256").EnumerateObject())
        {
            using Stream bytes = typeof(ReceiveDifferentialTests).Assembly.GetManifestResourceStream(prefix + file.Name)
                ?? throw new InvalidOperationException("Missing frozen oracle source: " + file.Name);
            Assert.Equal(file.Value.GetString(), Convert.ToHexStringLower(SHA256.HashData(bytes)));
        }
    }

    [Fact]
    public async Task FixedSeedsMatchFrozenFc21a49()
    {
        int count = int.TryParse(Environment.GetEnvironmentVariable("RECV1_SEEDS"), out int configured) ? configured : 2000;
        Assert.InRange(count, 1, int.MaxValue);
        int? selected = int.TryParse(Environment.GetEnvironmentVariable("RECV1_SEED"), out int requested) ? requested : null;
        Stopwatch time = Stopwatch.StartNew();
        for (int iteration = 0; iteration < count; iteration++)
        {
            int seed = selected ?? iteration;
            try { await CompareAsync(ReceiveSchedules.Random(seed)); }
            catch { output.WriteLine("Seed {0}, repetition {1}", seed, iteration); throw; }
        }
        output.WriteLine("{0} schedules, selected seed={1}, {2:F3} seconds", count, selected?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "0..N-1", time.Elapsed.TotalSeconds);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DelayedWriterCompletionDoesNotReleaseANewerParkedRead(bool notification)
    {
        ReceiveSchedule schedule = ReceiveSchedules.Named(10);
        Channel<ReadOnlyMemory<byte>> channel = Channel.CreateBounded<ReadOnlyMemory<byte>>(new BoundedChannelOptions(64)
        {
            SingleReader = true,
            SingleWriter = true,
            FullMode = BoundedChannelFullMode.Wait
        });
        TaskCompletionSource release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        ReceiveProbe probe = new(channel.Reader, new DelayedLastWriter(channel.Writer, release.Task),
            [.. schedule.Segments.Where(segment => segment.Protocol == ProtocolType.BlockFetch)],
            [.. schedule.Messages.Where(message => message.Protocol == ProtocolType.BlockFetch)]);
        using CancellationTokenSource cancellation = new();
        Task writer = Task.CompletedTask;
        Task reader = Task.CompletedTask;
        try
        {
            for (int i = 0; i < 64; i++) { await probe.Writer.WriteAsync(ReceiveVectors.Body(), cancellation.Token); }
            writer = probe.Writer.WriteAsync(ReceiveVectors.Body(), cancellation.Token).AsTask();
            Assert.False(writer.IsCompleted);
            for (int i = 0; i < 65; i++) { Assert.True(probe.Reader.TryRead(out _)); }
            Assert.Equal(65, probe.Admitted.Count);
            Assert.Equal(65, probe.Dequeued.Count);
            reader = notification ? probe.Reader.WaitToReadAsync(cancellation.Token).AsTask() : probe.Reader.ReadAsync(cancellation.Token).AsTask();
            Assert.Equal(1, probe.Parked);
            release.SetResult();
            await writer.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.False(reader.IsCompleted);
            Assert.Equal(0, probe.Reader.Count);
            Assert.Equal(1, probe.Parked);
        }
        finally
        {
            _ = release.TrySetResult();
            _ = probe.Writer.TryComplete();
            await cancellation.CancelAsync();
            _ = await Record.ExceptionAsync(() => Task.WhenAll(writer, reader).WaitAsync(TimeSpan.FromSeconds(10)));
        }
    }

    private sealed class DelayedLastWriter(ChannelWriter<ReadOnlyMemory<byte>> inner, Task release) : ChannelWriter<ReadOnlyMemory<byte>>
    {
        private int _writes;
        public override bool TryComplete(Exception? error = null) => inner.TryComplete(error);
        public override bool TryWrite(ReadOnlyMemory<byte> item) => inner.TryWrite(item);
        public override ValueTask<bool> WaitToWriteAsync(CancellationToken cancellationToken = default) => inner.WaitToWriteAsync(cancellationToken);
        public override async ValueTask WriteAsync(ReadOnlyMemory<byte> item, CancellationToken cancellationToken = default)
        {
            int index = ++_writes;
            await inner.WriteAsync(item, cancellationToken).ConfigureAwait(false);
            if (index == 65) { await release.WaitAsync(cancellationToken).ConfigureAwait(false); }
        }
    }

    [Fact]
    public void ManifestProjectsRawSegmentsAndWholeMessageItemsIndependently()
    {
        ReceiveSchedule prefix = ReceiveSchedules.Named(1);
        byte[] a = ReceiveVectors.Body();
        byte[] b = ReceiveVectors.Body(23);
        Assert.Equal(a.Concat(b.Take(2)), prefix.ItemBytes(0, wholeMessages: false));
        Assert.Equal(a, prefix.ItemBytes(0, wholeMessages: true));
        Assert.Equal(b.Concat(a), prefix.ItemBytes(1, wholeMessages: true));
        ReceiveSchedule spanning = ReceiveSchedules.Named(2);
        for (int segment = 0; segment < 99; segment++) { Assert.Empty(spanning.ItemBytes(segment, wholeMessages: true)); }
        Assert.Equal(ReceiveVectors.Body(256), spanning.ItemBytes(99, wholeMessages: true));
    }

    [Theory]
    [InlineData(1, (int)ReceiveConsumer.Buffer)]
    [InlineData(2, (int)ReceiveConsumer.Poll)]
    [InlineData(3, (int)ReceiveConsumer.Wait)]
    [InlineData(3, (int)ReceiveConsumer.All)]
    [InlineData(4, (int)ReceiveConsumer.Read)]
    [InlineData(5, (int)ReceiveConsumer.Read)]
    [InlineData(6, (int)ReceiveConsumer.Buffer)]
    [InlineData(7, (int)ReceiveConsumer.Buffer)]
    [InlineData(8, (int)ReceiveConsumer.Buffer)]
    [InlineData(8, (int)ReceiveConsumer.Poll)]
    [InlineData(9, (int)ReceiveConsumer.Buffer)]
    [InlineData(9, (int)ReceiveConsumer.Poll)]
    [InlineData(10, (int)ReceiveConsumer.Read)]
    [InlineData(11, (int)ReceiveConsumer.Read)]
    [InlineData(12, (int)ReceiveConsumer.Read)]
    [InlineData(13, (int)ReceiveConsumer.Buffer)]
    [InlineData(14, (int)ReceiveConsumer.Buffer)]
    [InlineData(15, (int)ReceiveConsumer.Buffer)]
    [InlineData(15, (int)ReceiveConsumer.Read)]
    public Task NamedCounterexamplesMatch(int number, int variantValue) =>
        CompareAsync(ReceiveSchedules.Named(number, (ReceiveConsumer)variantValue));

    [Theory]
    [InlineData("ready")]
    [InlineData("registration")]
    [InlineData("later")]
    public async Task H16SynchronousHandlerCannotStallOtherProtocols(string timing)
    {
        ReceiveSchedule schedule = new("H16-" + timing);
        byte[] message = ReceiveVectors.Body();
        foreach (ProtocolType protocol in new[] { ProtocolType.BlockFetch, ProtocolType.KeepAlive, ProtocolType.NodeChainSync })
        {
            schedule.Consumers[protocol] = ReceiveConsumer.Buffer;
            schedule.Stream(protocol, [message], message.Length);
        }
        WriteManifest(schedule);
        await using ReceiveDriver reference = new(schedule, frozen: true, start: false);
        await using ReceiveDriver current = new(schedule, frozen: false, start: false);
        string expected = await BlockedHandlerTraceAsync(reference, schedule, timing);
        string actual = await BlockedHandlerTraceAsync(current, schedule, timing);
        Assert.Equal(expected, actual);
    }

    private async Task<string> BlockedHandlerTraceAsync(ReceiveDriver driver, ReceiveSchedule schedule, string timing)
    {
        TaskCompletionSource release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        driver.Consumers[ProtocolType.KeepAlive].Call();
        driver.Consumers[ProtocolType.NodeChainSync].Call();
        Task handler = Task.CompletedTask;
        int firstEnd = schedule.Segments[0].WireEnd;
        byte[] wire = [.. schedule.Wire];
        try
        {
            handler = BlockHandlerAsync(driver.Consumers[ProtocolType.BlockFetch], entered, release.Task);
            driver.Bearer.Deliver(wire.AsSpan(0, firstEnd));
            if (timing == "ready") { driver.Bearer.Deliver(wire.AsSpan(firstEnd)); }
            if (timing == "registration") { driver.Bearer.CompleteNextDuringRegistration(wire.AsSpan(firstEnd).ToArray()); }
            driver.Start();
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            if (timing == "later")
            {
                await driver.QuietAsync();
                Assert.True(driver.Bearer.Pending);
                driver.Bearer.Deliver(wire.AsSpan(firstEnd));
            }
            await Task.WhenAll(driver.Consumers[ProtocolType.KeepAlive].Last, driver.Consumers[ProtocolType.NodeChainSync].Last)
                .WaitAsync(TimeSpan.FromSeconds(2));
            Assert.Equal("message", driver.Consumers[ProtocolType.KeepAlive].LastOutcome);
            Assert.Equal("message", driver.Consumers[ProtocolType.NodeChainSync].LastOutcome);
            Assert.True(driver.Bearer.Reads >= 2);
            if (timing == "ready") { Assert.True(driver.Bearer.SynchronousReads >= 2); }
            else { Assert.Equal(1, driver.Bearer.SynchronousReads); }
            Assert.Equal(timing == "registration" ? 1 : 0, driver.Bearer.RegistrationCompletions);
            await driver.QuietAsync();
            CheckMessages(driver, schedule);
            output.WriteLine("H16 timing={0}: other protocols progressed with handler blocked; reads={1}, synchronous={2}, registration={3}",
                timing, driver.Bearer.Reads, driver.Bearer.SynchronousReads, driver.Bearer.RegistrationCompletions);
            return JsonSerializer.Serialize(driver.Snapshot());
        }
        finally
        {
            release.SetResult();
            await handler.WaitAsync(TimeSpan.FromSeconds(10));
        }
    }

    private static async Task BlockHandlerAsync(ReceiveCalls consumer, TaskCompletionSource entered, Task release)
    {
        consumer.Call();
        await consumer.Last.ConfigureAwait(false);
        Assert.Equal("message", consumer.LastOutcome);
        entered.SetResult();
        BlockUntilReleased(release);
    }

    private static void BlockUntilReleased(Task release) =>
        Assert.True(release.Wait(TimeSpan.FromSeconds(10)), "H16 cleanup did not release the blocking handler");

    private async Task CompareAsync(ReceiveSchedule schedule)
    {
        WriteManifest(schedule);
        await using ReceiveDriver reference = new(schedule, frozen: true);
        await using ReceiveDriver current = new(schedule, frozen: false);
        int delivered = 0;
        try
        {
            await BothQuietAsync();
            byte[] wire = [.. schedule.Wire];
            for (int step = 0; step < schedule.Steps.Count; step++)
            {
                ReceiveStep action = schedule.Steps[step];
                if (action.Action == ReceiveAction.Deliver)
                {
                    reference.Bearer.Deliver(wire.AsSpan(delivered, action.Bytes));
                    current.Bearer.Deliver(wire.AsSpan(delivered, action.Bytes));
                    delivered += action.Bytes;
                }
                else
                {
                    Apply(reference, action);
                    Apply(current, action);
                }
                if (schedule.Name.StartsWith("H1-", StringComparison.Ordinal) && action is { Action: ReceiveAction.Call, Protocol: ProtocolType.BlockFetch } &&
                    current.Consumers[action.Protocol].ReturnCheckpoints.Count == 1)
                {
                    // A's handler waits for keep-alive before requesting the next fetch message.
                    await Task.WhenAll(reference.Consumers[ProtocolType.KeepAlive].Last, current.Consumers[ProtocolType.KeepAlive].Last)
                        .WaitAsync(TimeSpan.FromSeconds(2));
                    Assert.Equal("item", reference.Consumers[ProtocolType.KeepAlive].LastOutcome);
                    Assert.Equal("item", current.Consumers[ProtocolType.KeepAlive].LastOutcome);
                }
                if (schedule.Name.StartsWith("H4-", StringComparison.Ordinal) && action.Action == ReceiveAction.Call &&
                    current.Consumers[action.Protocol].ReturnCheckpoints.Count == 1)
                {
                    Assert.InRange(current.Consumers[action.Protocol].ReturnCheckpoints[0], 0, (256 * 1024) + 65543);
                    output.WriteLine("H4 return checkpoint: frozen caller bytes={0}, current caller bytes={1}; shared-reader budget enforced",
                        reference.Consumers[action.Protocol].ReturnCheckpoints[0], current.Consumers[action.Protocol].ReturnCheckpoints[0]);
                }
                await BothQuietAsync();
                CompareSnapshots(reference, current, schedule, step, action);
                CheckMessages(reference, schedule);
                CheckMessages(current, schedule);
                if (schedule.Name.StartsWith("H3-", StringComparison.Ordinal) && action.Action == ReceiveAction.Deliver)
                {
                    ReceiveProbe probe = current.Probes[ProtocolType.BlockFetch];
                    bool notification = schedule.Consumers[ProtocolType.BlockFetch] == ReceiveConsumer.Wait;
                    Assert.Equal(notification ? 64 : 65, probe.Admitted.Count);
                    Assert.Equal(notification ? 0 : 1, probe.Dequeued.Count);
                    Assert.Equal(64, probe.Reader.Count);
                }
            }
            if (schedule.DeclaredDifference is string difference)
            {
                Assert.True(current.Run.IsFaulted, "The declared connection fault must actually occur: " + difference);
                Type cause = difference.StartsWith("EOF", StringComparison.Ordinal) ? typeof(EndOfStreamException)
                    : difference.StartsWith("Invalid", StringComparison.Ordinal) ? typeof(SAIB.Cbor.CborException) : typeof(InvalidDataException);
                Assert.Equal(cause, current.Run.Exception!.InnerException!.GetType());
            }
            if (schedule.Name.StartsWith("H13-", StringComparison.Ordinal))
            {
                Assert.True(reference.Bearer.Spinning);
                Assert.False(current.Bearer.Spinning);
                _ = Assert.IsType<EndOfStreamException>(current.Run.Exception?.InnerException);
            }
            if (schedule.Name.StartsWith("H2-", StringComparison.Ordinal)) { Assert.Equal(100, current.Probes[ProtocolType.BlockFetch].Dequeued.Count); }
            if (schedule.Name.StartsWith("H10-", StringComparison.Ordinal))
            {
                Assert.Equal(64, current.Probes[ProtocolType.BlockFetch].Reader.Count);
                Assert.True(current.Consumers[ProtocolType.KeepAlive].Pending);
            }
            if (schedule.DeclaredDifference is not null) { output.WriteLine("{0}: asserted shared-reader difference: {1}", schedule.Name, schedule.DeclaredDifference); }
        }
        catch
        {
            output.WriteLine("Failure manifest: {0}", schedule.Manifest());
            throw;
        }

        Task BothQuietAsync() => Task.WhenAll(reference.QuietAsync(), current.QuietAsync());
    }

    private static void CompareSnapshots(ReceiveDriver reference, ReceiveDriver current, ReceiveSchedule schedule, int step, ReceiveStep action)
    {
        JsonNode expected = JsonSerializer.SerializeToNode(reference.Snapshot())!;
        JsonNode actual = JsonSerializer.SerializeToNode(current.Snapshot())!;
        if (schedule.DeclaredDifference is string difference && current.Run.IsFaulted)
        {
            Exception cause = current.Run.Exception!.InnerException!;
            if (difference.StartsWith("EOF", StringComparison.Ordinal))
            {
                _ = Assert.IsType<EndOfStreamException>(cause);
                Assert.True(expected["End"]!.GetValue<string>() is "spin" or "clean");
                expected["End"] = "declared EOF";
                actual["End"] = "declared EOF";
                expected["Cause"] = null;
                actual["Cause"] = null;
            }
            else
            {
                Type type = difference.StartsWith("Invalid", StringComparison.Ordinal) ? typeof(SAIB.Cbor.CborException) : typeof(InvalidDataException);
                Assert.Equal(type, cause.GetType());
                int rejected = difference.StartsWith("Invalid", StringComparison.Ordinal) ? 0 : schedule.Messages.Single().LastSegment;
                foreach (ProtocolType protocol in schedule.Consumers.Keys)
                {
                    Assert.Equal(reference.Probes[protocol].Admitted.Where(index => index < rejected), current.Probes[protocol].Admitted);
                    Assert.Equal(reference.Probes[protocol].Dequeued.Where(index => index < rejected), current.Probes[protocol].Dequeued);
                    Assert.Equal(0, current.Probes[protocol].Reader.Count);
                    Assert.Equal(typeof(ConnectionClosedException).FullName, current.Consumers[protocol].LastOutcome);
                }
                return; // Only the explicitly rejected segment/message and its consequent end differ.
            }
        }
        Assert.True(JsonNode.DeepEquals(expected, actual), $"{schedule.Name}, step {step}, {action}:\nreference={expected}\ncurrent={actual}");
    }

    private static void CheckMessages(ReceiveDriver driver, ReceiveSchedule schedule)
    {
        foreach ((ProtocolType protocol, ReceiveCalls consumer) in driver.Consumers) { consumer.CheckMessages(schedule, protocol); }
    }

    private static void Apply(ReceiveDriver driver, ReceiveStep step)
    {
        switch (step.Action)
        {
            case ReceiveAction.Call: driver.Consumers[step.Protocol].Call(); break;
            case ReceiveAction.Cancel: driver.Consumers[step.Protocol].Cancel(); break;
            case ReceiveAction.End: driver.Bearer.Deliver([], end: true); break;
            case ReceiveAction.Dispose: driver.Close(); break;
            case ReceiveAction.Deliver: throw new InvalidOperationException("Deliveries require the manifest wire slice");
            default: throw new InvalidOperationException();
        }
    }

    private static void WriteManifest(ReceiveSchedule schedule)
    {
        string? directory = Environment.GetEnvironmentVariable("RECV1_MANIFEST_DIR");
        if (directory is null) { return; }
        _ = Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, schedule.Name + ".json"), schedule.Manifest());
    }
}
