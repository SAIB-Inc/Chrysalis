using System.Buffers;
using System.IO.Pipelines;
using System.Security.Cryptography;
using System.Text.Json;
using Chrysalis.Codec.Extensions.Cardano.Core;
using Chrysalis.Codec.Serialization;
using Chrysalis.Codec.Types;
using Chrysalis.Codec.Types.Cardano.Core;
using Chrysalis.Network.Cbor.BlockFetch;
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
        Check(nameof(NoBlocksIsNotCounted), NoBlocksIsNotCounted, failures);
        Check(nameof(DuplicateBodiesAreRejectedAfterDraining), DuplicateBodiesAreRejectedAfterDraining, failures);
        Check(nameof(OneBodyIsCounted), OneBodyIsCounted, failures);
        Check(nameof(OpaqueDefiniteBytesDoNotAllocate), OpaqueDefiniteBytesDoNotAllocate, failures);
        Check(nameof(OpaqueBytesRoundTrip), OpaqueBytesRoundTrip, failures);
        Check(nameof(CorpusTipKeepsThePoint), CorpusTipKeepsThePoint, failures);
        Check(nameof(CorpusTipRejectsIncompleteTips), CorpusTipRejectsIncompleteTips, failures);
        await CheckAsync(nameof(CaptureDrainsAFullQueue), CaptureDrainsAFullQueue, failures).ConfigureAwait(false);
        await CheckAsync(nameof(CaptureRequiresPriorDeclarationBeforeConnecting), CaptureRequiresPriorDeclarationBeforeConnecting, failures).ConfigureAwait(false);
        if (failures.Count != 0) { throw new AggregateException(failures); }
    }

    private static void Check(string name, Action test, List<Exception> failures)
    {
        try { test(); Console.WriteLine("PASS " + name); }
        catch (InvalidDataException exception) { failures.Add(exception); Console.Error.WriteLine("FAIL " + name + ": " + exception.Message); }
    }

    private static async Task CheckAsync(string name, Func<Task> test, List<Exception> failures)
    {
        try { await test().ConfigureAwait(false); Console.WriteLine("PASS " + name); }
        catch (Exception exception) when (exception is InvalidDataException or OperationCanceledException or TimeoutException)
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

    private static async Task CaptureRequiresPriorDeclarationBeforeConnecting()
    {
        string root = Path.Combine(Fixtures.Root, "..", "out", "capture-check-" + Guid.NewGuid().ToString("N"));
        _ = Directory.CreateDirectory(root);
        string anchorPath = Path.Combine(root, "anchor.json");
        string declarationPath = Path.Combine(root, "declaration.md");
        string output = Path.Combine(root, "output");
        try
        {
            await File.WriteAllTextAsync(anchorPath, JsonSerializer.Serialize(new
            {
                anchor = new RangePoint(1, new string('0', 64), ""),
                blockNumber = 1,
                observedUtc = DateTimeOffset.Parse("2026-01-03T00:04:42Z", System.Globalization.CultureInfo.InvariantCulture)
            })).ConfigureAwait(false);
            string declaration = "- Declaration time: 2026-01-03T00:04:42Z\n- T0 = 2026-01-01T00:00:00Z\n- Mainnet slot at T0: 2.\n";
            DateTimeOffset after = DateTimeOffset.Parse("2026-01-03T00:04:43Z", System.Globalization.CultureInfo.InvariantCulture);
            if (CorpusCapture.ReadDeclaration(declaration, after).FirstSlot != 2)
            {
                throw new InvalidDataException("The declaration boundary was not preserved.");
            }
            await File.WriteAllTextAsync(declarationPath, declaration).ConfigureAwait(false);
            try { await CorpusCapture.RunAsync(["capture", "unused", "3001", output, anchorPath, declarationPath]).ConfigureAwait(false); }
            catch (InvalidDataException)
            {
                if (Directory.Exists(output)) { throw new InvalidDataException("A capture without a prior declaration created an attempt."); }
                return;
            }
            throw new InvalidDataException("A capture whose declaration did not precede lookup was accepted.");
        }
        finally { Directory.Delete(root, true); }
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
