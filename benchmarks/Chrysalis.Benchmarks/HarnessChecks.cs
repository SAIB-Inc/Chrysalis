using System.Buffers;
using System.IO.Pipelines;
using System.Security.Cryptography;
using System.Text.Json;
using Chrysalis.Codec.Extensions.Cardano.Core;
using Chrysalis.Codec.Serialization;
using Chrysalis.Codec.Types;
using Chrysalis.Codec.Types.Cardano.Core;
using Chrysalis.Network.Cbor.BlockFetch;
using Chrysalis.Network.Cbor.ChainSync;
using Chrysalis.Network.Cbor.KeepAlive;
using Chrysalis.Network.Core;
using Chrysalis.Network.MiniProtocols;
using Chrysalis.Network.Multiplexer;
using Chrysalis.Network.Cbor.Common;

namespace Chrysalis.Benchmarks;

internal static class HarnessChecks
{
    internal static async Task RunAsync()
    {
        List<Exception> failures = [];
        Check(nameof(DirectFilterPreservesSinglePatterns), DirectFilterPreservesSinglePatterns, failures);
        Check(nameof(DirectFilterListsMatchUnion), DirectFilterListsMatchUnion, failures);
        Check(nameof(NoBlocksIsNotCounted), NoBlocksIsNotCounted, failures);
        Check(nameof(DuplicateBodiesAreRejectedAfterDraining), DuplicateBodiesAreRejectedAfterDraining, failures);
        Check(nameof(OneBodyIsCounted), OneBodyIsCounted, failures);
        Check(nameof(OpaqueDefiniteBytesDoNotAllocate), OpaqueDefiniteBytesDoNotAllocate, failures);
        Check(nameof(OpaqueBytesRoundTrip), OpaqueBytesRoundTrip, failures);
        Check(nameof(CorpusTipKeepsThePoint), CorpusTipKeepsThePoint, failures);
        Check(nameof(CorpusTipRejectsIncompleteTips), CorpusTipRejectsIncompleteTips, failures);
        await CheckAsync(nameof(CaptureDrainsAFullQueue), CaptureDrainsAFullQueue, failures).ConfigureAwait(false);
        Check(nameof(CaptureDeclarationMatchesClock), CaptureDeclarationMatchesClock, failures);
        await CheckAsync(nameof(CaptureRejectsInvalidAnchorBeforeConnecting), CaptureRejectsInvalidAnchorBeforeConnecting, failures).ConfigureAwait(false);
        await CheckAsync(nameof(CaptureRejectsFirstHeaderBeforeBoundary), CaptureRejectsFirstHeaderBeforeBoundary, failures).ConfigureAwait(false);
        await CheckAsync(nameof(CaptureRequiresPriorDeclarationBeforeConnecting), CaptureRequiresPriorDeclarationBeforeConnecting, failures).ConfigureAwait(false);
        if (failures.Count != 0) { throw new AggregateException(failures); }
    }

    private static void Check(string name, Action test, List<Exception> failures)
    {
        try { test(); Console.WriteLine("PASS " + name); }
        catch (InvalidDataException exception) { failures.Add(exception); Console.Error.WriteLine("FAIL " + name + ": " + exception.Message); }
    }

    private static void DirectFilterPreservesSinglePatterns()
    {
        foreach (string pattern in new[] { "*", "CoreBench.*", "?oreBench.Sign", "CoreBench.Sign", "absent" })
        {
            foreach (string name in new[] { "CoreBench.Sign", "CoreBench.Verify", "TxBench.SimpleBuildSign" })
            {
                if (DirectMeasurements.Matches(pattern, name) != System.IO.Enumeration.FileSystemName.MatchesSimpleExpression(pattern, name))
                {
                    throw new InvalidDataException("Single direct filter changed its match set.");
                }
            }
        }
    }

    private static void DirectFilterListsMatchUnion()
    {
        foreach (string name in new[] { "CoreBench.Sign", "CoreBench.Verify", "TxBench.SimpleBuildSign" })
        {
            bool expected = DirectMeasurements.Matches("CoreBench.Sign", name) || DirectMeasurements.Matches("TxBench.*", name);
            if (DirectMeasurements.Matches("CoreBench.Sign,TxBench.*", name) != expected)
            {
                throw new InvalidDataException("Direct filter list did not match the union.");
            }
        }
    }

    private static async Task CheckAsync(string name, Func<Task> test, List<Exception> failures)
    {
        try { await test().ConfigureAwait(false); Console.WriteLine("PASS " + name); }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            failures.Add(exception); Console.Error.WriteLine("FAIL " + name + ": " + exception.GetType().Name);
        }
    }

    private static async Task CaptureDrainsAFullQueue()
    {
        byte[] bytes = Fixtures.Load("block-*.cbor")[0];
        BlockWithEra block = CborSerializer.Deserialize<BlockWithEra>(bytes);
        RangePoint point = new(block.Block.Slot(), block.Block.Header().Hash(), Convert.ToHexStringLower(SHA256.HashData(bytes)));
        FixedRange range = new(point, [.. Enumerable.Repeat(point, 21)]);
        byte[] body = CborSerializer.Serialize(new BlockBody(4, new CborEncodedValue(bytes)));
        byte[] rest = [.. Enumerable.Repeat(body, 20).SelectMany(value => value), 0x81, 0x05];
        using OfflineReplay.ReplayBearer bearer = new();
        using Plexer plexer = new(bearer);
        BlockFetch fetch = new(plexer.SubscribeClient(ProtocolType.BlockFetch));
        KeepAliveClient keepAlive = new(plexer.SubscribeClient(ProtocolType.KeepAlive));
        CorpusCapture.Heartbeat heartbeat = new(keepAlive, TimeSpan.Zero);
        using CancellationTokenSource cancellation = new(TimeSpan.FromSeconds(30));
        Task worker = plexer.RunAsync(cancellation.Token);
        Task relay = RelayAsync();
        try
        {
            CorpusCapture.CapturedBlock[] captured = await CorpusCapture.FetchAsync(fetch, heartbeat, range, cancellation.Token).ConfigureAwait(false);
            if (captured.Length != 21 || captured.Any(value => value.Bytes != bytes.Length || value.Sha256 != point.Sha256))
            {
                throw new InvalidDataException("The capture did not yield every fixture with its digest.");
            }
        }
        finally
        {
            await cancellation.CancelAsync().ConfigureAwait(false);
            foreach (Task task in new[] { relay, worker })
            {
                try { await task.ConfigureAwait(false); }
                catch (OperationCanceledException) { }
            }
        }

        async Task RelayAsync()
        {
            _ = await ReadRequestAsync(bearer.Output, cancellation.Token).ConfigureAwait(false);
            await SendAsync(ProtocolType.BlockFetch, new byte[] { 0x81, 0x02 }).ConfigureAwait(false);
            await SendAsync(ProtocolType.BlockFetch, body).ConfigureAwait(false);
            MuxSegment request = await ReadRequestAsync(bearer.Output, cancellation.Token).ConfigureAwait(false);
            MessageKeepAlive first = CborSerializer.Deserialize<MessageKeepAlive>(request.Payload.ToArray());
            Console.WriteLine("Fake relay: first block consumed; 80 BlockFetch frames precede the keep-alive reply.");
            for (int i = 0; i < 80; i++) { await SendAsync(ProtocolType.BlockFetch, rest.AsMemory(i * 1024, 1024)).ConfigureAwait(false); }
            await ReplyAsync(first.Cookie).ConfigureAwait(false);
            for (int offset = 80 * 1024; offset < rest.Length; offset += 1024)
            {
                await SendAsync(ProtocolType.BlockFetch, rest.AsMemory(offset, Math.Min(1024, rest.Length - offset))).ConfigureAwait(false);
            }
            while (true)
            {
                request = await ReadRequestAsync(bearer.Output, cancellation.Token).ConfigureAwait(false);
                MessageKeepAlive ping = CborSerializer.Deserialize<MessageKeepAlive>(request.Payload.ToArray());
                await ReplyAsync(ping.Cookie).ConfigureAwait(false);
            }
        }

        Task ReplyAsync(uint cookie) => SendAsync(ProtocolType.KeepAlive, CborSerializer.Serialize(new MessageKeepAliveResponse(1, cookie)));

        async Task SendAsync(ProtocolType protocol, ReadOnlyMemory<byte> payload)
        {
            byte[] frame = MuxSegmentCodec.Encode(new MuxSegment(new MuxSegmentHeader(0, protocol, checked((ushort)payload.Length), true),
                new ReadOnlySequence<byte>(payload))).ToArray();
            _ = await bearer.Input.WriteAsync(frame, cancellation.Token).ConfigureAwait(false);
        }
    }

    private static async Task<MuxSegment> ReadRequestAsync(PipeReader reader, CancellationToken token)
    {
        while (true)
        {
            ReadResult result = await reader.ReadAsync(token).ConfigureAwait(false);
            ReadOnlySequence<byte> buffer = result.Buffer;
            if (buffer.Length >= 8)
            {
                MuxSegmentHeader header = MuxSegmentCodec.DecodeHeader(buffer.Slice(0, 8));
                if (buffer.Length >= 8 + header.PayloadLength)
                {
                    byte[] payload = buffer.Slice(8, header.PayloadLength).ToArray();
                    reader.AdvanceTo(buffer.GetPosition(8 + header.PayloadLength));
                    return new(header, new ReadOnlySequence<byte>(payload));
                }
            }
            reader.AdvanceTo(buffer.Start, buffer.End);
            if (result.IsCompleted) { throw new InvalidDataException("Fake relay request stream ended."); }
        }
    }

    private const string Declaration = "- Declaration time: 2026-11-16T23:04:42Z\n- T0 = 2026-11-14T23:00:00Z\n- Mainnet slot at T0: 203130909.\n";

    private static void CaptureDeclarationMatchesClock()
    {
        DateTimeOffset after = DateTimeOffset.Parse("2026-11-17T01:06:44Z", System.Globalization.CultureInfo.InvariantCulture);
        if (CorpusCapture.ReadDeclaration(Declaration, after).FirstSlot != 203130909)
        {
            throw new InvalidDataException("The declaration boundary was not preserved.");
        }
        foreach (string invalid in new[] {
            Declaration.Replace("203130909", "203130910", StringComparison.Ordinal),
            Declaration.Replace("23:00:00Z", "22:00:00Z", StringComparison.Ordinal),
            Declaration.Replace("23:00:00Z", "22:00:00Z", StringComparison.Ordinal).Replace("203130909", "203127309", StringComparison.Ordinal) })
        {
            try { _ = CorpusCapture.ReadDeclaration(invalid, after); }
            catch (InvalidDataException) { continue; }
            throw new InvalidDataException("A declaration contradicting its clock or slot was accepted.");
        }
    }

    private static Task CaptureRequiresPriorDeclarationBeforeConnecting() => CaptureRejectsBeforeConnecting(
        203130896, DateTimeOffset.Parse("2026-11-16T23:04:42Z", System.Globalization.CultureInfo.InvariantCulture),
        "The declaration must fix the 48-hour, hour-rounded T0 before the first fetch.");

    private static async Task CaptureRejectsInvalidAnchorBeforeConnecting()
    {
        await CaptureRejectsBeforeConnecting(203130909,
            DateTimeOffset.Parse("2026-11-17T01:06:44Z", System.Globalization.CultureInfo.InvariantCulture),
            "The anchor lookup or anchor slot does not match the declaration.").ConfigureAwait(false);
        await CaptureRejectsBeforeConnecting(203130896, DateTimeOffset.UtcNow.AddDays(1),
            "The anchor lookup or anchor slot does not match the declaration.").ConfigureAwait(false);
    }

    private static async Task CaptureRejectsBeforeConnecting(ulong slot, DateTimeOffset observedUtc, string message)
    {
        string root = Path.Combine(Fixtures.Root, "..", "out", "capture-check-" + Guid.NewGuid().ToString("N"));
        _ = Directory.CreateDirectory(root);
        string anchorPath = Path.Combine(root, "anchor.json");
        string declarationPath = Path.Combine(root, "declaration.md");
        string output = Path.Combine(root, "output");
        bool connected = false;
        try
        {
            await File.WriteAllTextAsync(anchorPath, JsonSerializer.Serialize(new
            {
                anchor = new RangePoint(slot, new string('0', 64), ""),
                blockNumber = 1,
                observedUtc
            })).ConfigureAwait(false);
            await File.WriteAllTextAsync(declarationPath, Declaration).ConfigureAwait(false);
            try
            {
                await CorpusCapture.RunAsync(["capture", "unused", "3001", output, anchorPath, declarationPath],
                    (_, _, _, _) => { connected = true; throw new InvalidOperationException("Offline connection sentinel."); }).ConfigureAwait(false);
            }
            catch (InvalidDataException exception) when (exception.Message == message)
            {
                if (connected || Directory.Exists(output)) { throw new InvalidDataException("Invalid capture inputs reached connection setup."); }
                return;
            }
            throw new InvalidDataException("Invalid capture inputs were accepted.");
        }
        finally { Directory.Delete(root, true); }
    }

    private static async Task CaptureRejectsFirstHeaderBeforeBoundary()
    {
        BlockWithEra block = CborSerializer.Deserialize<BlockWithEra>(Fixtures.Load("block-*.cbor")[0]);
        byte[] header = block.Block.Header().Raw.ToArray();
        byte[] wrapped = [0x82, 0x06, 0xd8, 0x18, 0x59, (byte)(header.Length >> 8), (byte)header.Length, .. header];
        N2NBlockHeader payload = CborSerializer.Deserialize<N2NBlockHeader>(wrapped);
        RangePoint anchor = new(block.Block.Slot() - 1, new string('0', 64), "");
        Tip tip = new(anchor.ToPoint(), 1);
        using OfflineReplay.ReplayBearer bearer = new();
        using Plexer plexer = new(bearer);
        ChainSync chain = new(plexer.SubscribeClient(ProtocolType.NodeChainSync), ProtocolType.NodeChainSync);
        CorpusCapture.Heartbeat heartbeat = new(new KeepAliveClient(plexer.SubscribeClient(ProtocolType.KeepAlive)), TimeSpan.FromHours(1));
        foreach (MessageNextResponse reply in new MessageNextResponse[] {
            new MessageRollBackward(3, anchor.ToPoint(), tip), new N2NMessageRollForward(2, payload, tip),
            new MessageRollBackward(3, anchor.ToPoint(), tip) })
        {
            byte[] bytes = CborSerializer.Serialize(reply);
            byte[] frame = MuxSegmentCodec.Encode(new MuxSegment(new MuxSegmentHeader(0, ProtocolType.NodeChainSync,
                checked((ushort)bytes.Length), true), new ReadOnlySequence<byte>(bytes))).ToArray();
            _ = await bearer.Input.WriteAsync(frame).ConfigureAwait(false);
        }
        using CancellationTokenSource cancellation = new(TimeSpan.FromSeconds(5));
        Task worker = plexer.RunAsync(cancellation.Token);
        try
        {
            try { _ = await CorpusCapture.ReadHeadersAsync(chain, heartbeat, anchor, null, block.Block.Slot() + 1, cancellation.Token).ConfigureAwait(false); }
            catch (InvalidDataException exception) when (exception.Message == "The first block precedes the declaration boundary.") { return; }
            throw new InvalidDataException("A first header below T0 was accepted.");
        }
        finally
        {
            await cancellation.CancelAsync().ConfigureAwait(false);
            try { await worker.ConfigureAwait(false); }
            catch (OperationCanceledException) { }
        }
    }

    private static void CorpusTipKeepsThePoint()
    {
        byte[] hash = [.. Enumerable.Range(0, 32).Select(value => (byte)value)];
        RangePoint point = CorpusCapture.TipPoint(new Tip(Point.Specific(123, hash), 456));
        if (point.Slot != 123 || point.Hash != Convert.ToHexStringLower(hash) || point.Sha256.Length != 0)
        {
            throw new InvalidDataException("A tip record changed the relay point.");
        }
    }

    private static void CorpusTipRejectsIncompleteTips()
    {
        foreach (Tip tip in new[] { new Tip(Point.Origin, null), new Tip(Point.Specific(1, new byte[31]), 1),
            new Tip(Point.Specific(1, new byte[32]), null), new Tip(Point.Specific(1, new byte[32]), 0) })
        {
            try { _ = CorpusCapture.TipPoint(tip); }
            catch (InvalidDataException) { continue; }
            throw new InvalidDataException("An incomplete tip was accepted.");
        }
    }

    private static void NoBlocksIsNotCounted()
    {
        SingleBlockCounter counter = default;
        if (counter.BodyCount != 0) { throw new InvalidDataException("A new counter already has a body."); }
        try { _ = counter.Complete(); }
        catch (InvalidDataException) { return; }
        throw new InvalidDataException("A zero-body single-point response was accepted.");
    }

    private static void DuplicateBodiesAreRejectedAfterDraining()
    {
        SingleBlockCounter counter = default;
        counter.Record(10);
        counter.Record(20); // Must not throw: the caller must drain the rest of the batch.
        try { _ = counter.Complete(); }
        catch (InvalidDataException) { return; }
        throw new InvalidDataException("A duplicate-body single-point response was accepted.");
    }

    private static void OneBodyIsCounted()
    {
        SingleBlockCounter counter = default;
        counter.Record(123);
        if (counter.Complete() != 123) { throw new InvalidDataException("One body was not counted correctly."); }
    }

    private static void OpaqueDefiniteBytesDoNotAllocate()
    {
        byte[] bytes = [0x44, 1, 2, 3, 4];
        for (int i = 0; i < 1000; i++) { ResultSink<long>.Value = RawCbor.ReadEveryToken(bytes); }
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 100; i++) { ResultSink<long>.Value = RawCbor.ReadEveryToken(bytes); }
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        if (allocated != 0) { throw new InvalidDataException($"Opaque definite-byte traversal allocated {allocated} bytes instead of zero."); }
    }

    private static void OpaqueBytesRoundTrip()
    {
        byte[] definite = [0x44, 1, 2, 3, 4];
        byte[] indefinite = [0x5f, 0x42, 1, 2, 0x42, 3, 4, 0xff];
        if (RawCbor.ReadEveryToken(definite) != 11 || RawCbor.ReadEveryToken(indefinite) != 11
            || !RawCbor.EncodeEveryToken(definite).Span.SequenceEqual(definite)
            || !RawCbor.EncodeEveryToken(indefinite).Span.SequenceEqual(definite))
        {
            throw new InvalidDataException("Opaque byte-string traversal or re-encoding changed contents.");
        }
    }
}
