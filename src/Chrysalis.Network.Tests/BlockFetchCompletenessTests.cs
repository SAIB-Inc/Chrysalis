using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading.Channels;
using Chrysalis.Codec.Extensions.Cardano.Core;
using Chrysalis.Codec.Extensions.Cardano.Core.Header;
using Chrysalis.Codec.Serialization;
using Chrysalis.Codec.Types.Cardano.Core;
using Chrysalis.Codec.Types;
using Chrysalis.Network.Cbor.BlockFetch;
using Chrysalis.Network.Cbor.ChainSync;
using Chrysalis.Network.Core;
using Chrysalis.Network.Multiplexer;
using SAIB.Cbor;
using SAIB.Cbor.Serialization;
using Xunit;
using Xunit.Abstractions;

namespace Chrysalis.Network.Tests;

// First-chance callbacks are process-wide, so no unrelated throwing test may run alongside these.
[CollectionDefinition("BlockFetch completeness", DisableParallelization = true)]
public class BlockFetchCompletenessIsolation;

[Collection("BlockFetch completeness")]
public class BlockFetchCompletenessTests(ITestOutputHelper output)
{
    private delegate bool CompletenessProbe(ReadOnlySpan<byte> data);

    [Fact]
    public async Task RecordedIncompleteBodiesNeverThrowBeforeCompletion()
    {
        Recording recording = Recording.Load();
        using ProtocolReplay replay = new(ProtocolType.BlockFetch, recording.FetchSegments);
        ChannelBuffer buffer = new(replay.Channel);
        using CancellationTokenSource deadline = new(TimeSpan.FromSeconds(30));
        int throws = 0;
        void Count(object? sender, FirstChanceExceptionEventArgs args)
        {
            if (args.Exception is CborException) { _ = Interlocked.Increment(ref throws); }
        }
        AppDomain.CurrentDomain.FirstChanceException += Count;
        try
        {
            _ = Assert.IsType<StartBatch>(await buffer.ReceiveFullMessageAsync<BlockFetchMessage>(deadline.Token));
            foreach (string digest in recording.Digests)
            {
                BlockBody body = Assert.IsType<BlockBody>(await buffer.ReceiveFullMessageAsync<BlockFetchMessage>(deadline.Token));
                Assert.Equal(digest, Convert.ToHexStringLower(SHA256.HashData(body.Body.Value.Span)));
            }
            if (recording.Digests.Length == 1000)
            {
                _ = Assert.IsType<BatchDone>(await buffer.ReceiveFullMessageAsync<BlockFetchMessage>(deadline.Token));
            }
        }
        finally { AppDomain.CurrentDomain.FirstChanceException -= Count; }
        output.WriteLine($"Validated {recording.Digests.Length} byte-identical bodies; first-chance CborExceptions={throws}.");
        Assert.Equal(0, throws);
    }

    // Regression guard: passes on baseline 7521d26 by design; preserves captured bytes and the independent predecessor-hash checks.
    [Fact]
    public async Task RecordedBodiesMatchSha256RawHeadersAndNodePredecessorHashes()
    {
        Recording recording = Recording.Load();
        using ProtocolReplay fetch = new(ProtocolType.BlockFetch, recording.FetchSegments);
        using ProtocolReplay chain = new(ProtocolType.NodeChainSync, recording.ChainSegments);
        ChannelBuffer bodies = new(fetch.Channel);
        ChannelBuffer headers = new(chain.Channel);
        using CancellationTokenSource deadline = new(TimeSpan.FromSeconds(30));
        _ = Assert.IsType<StartBatch>(await bodies.ReceiveFullMessageAsync<BlockFetchMessage>(deadline.Token));
        byte[] previousHash = Convert.FromHexString("e5c46f069110d4f8d00a126fd06a783c0eb0a72fe4b9b60be3b3701b1d0b538b");
        ulong lastBlockSlot = 0;
        ulong? lastTipSlot = null;
        for (int i = 0; i < recording.Digests.Length; i++)
        {
            N2NMessageRollForward? forward = null;
            while (forward is null)
            {
                MessageNextResponse next = await headers.ReceiveFullMessageAsync<MessageNextResponse>(deadline.Token);
                if (next is MessageRollBackward backward)
                {
                    Cbor.Common.SpecificPoint anchor = Assert.IsType<Cbor.Common.SpecificPoint>(backward.Point);
                    Assert.Equal(139055739UL, anchor.Slot);
                    Assert.Equal(previousHash, anchor.Hash.ToArray());
                    AssertNodeTip(backward.Tip);
                }
                forward = next as N2NMessageRollForward;
            }
            ChainSyncHeader header = ChainSyncHeader.Decode(forward.Payload.Raw);
            BlockBody body = Assert.IsType<BlockBody>(await bodies.ReceiveFullMessageAsync<BlockFetchMessage>(deadline.Token));
            Assert.Equal(recording.Digests[i], Convert.ToHexStringLower(SHA256.HashData(body.Body.Value.Span)));
            BlockWithEra block = CborSerializer.Deserialize<BlockWithEra>(body.Body.Value);
            Assert.Equal(header.HeaderCbor.ToArray(), block.Block.Header().Raw.ToArray());
            // Both points below are Chrysalis-computed from node-supplied bytes. Agreement is
            // an internal consistency check, not an independent node-supplied hash oracle.
            ChainPoint computedHeaderPoint = header.ExtractPoint();
            Assert.Equal(computedHeaderPoint.Slot, block.Block.Slot());
            // Read the two node-supplied integers directly, without the lazy HeaderBody accessor.
            (ulong height, ulong slot) = ReadHeaderNumbers(header.HeaderCbor);
            Assert.Equal(slot, computedHeaderPoint.Slot);
            Assert.Equal(height, computedHeaderPoint.BlockNumber);
            AssertNodeTip(forward.Tip);
            byte[] hash = Convert.FromHexString(block.Block.Hash());
            Assert.Equal(computedHeaderPoint.Hash, hash);
            // The NODE-SUPPLIED predecessor field independently checks the preceding computed
            // digest (or the recorded anchor for the first block). The last block has no following
            // header in this capture, so its computed digest has no predecessor-field oracle.
            Assert.Equal(previousHash, block.Block.Header().HeaderBody.PrevHash()!.Value.ToArray());
            previousHash = hash;
            lastBlockSlot = block.Block.Slot();
            if (forward.Tip.Slot is Cbor.Common.SpecificPoint point) { lastTipSlot = point.Slot; }
        }
        // The range's last digest has no following-header oracle. Pin it separately with
        // Python 3.12 hashlib.blake2b(header_bytes, digest_size=32), not Chrysalis.
        string finalHash = recording.Digests.Length == 1000
            ? "e63ff6eea80642d5be9ed6f9c6da6fc5532afd91b34035a786eaafc165775872"
            : "e69599a62fe464a45aac102fe9d7181da2d4d9bae7b03102c3262c2be7036f8c";
        Assert.Equal(finalHash, Convert.ToHexStringLower(previousHash));
        output.WriteLine($"Validated {recording.Digests.Length} ExtractPoint slot/height/hash values, body SHA256s and node-supplied raw headers; {recording.Digests.Length - 1} node-supplied predecessor hashes plus the anchor; final digest independently pinned with Python hashlib. All node Tips/rollback points pinned. Last block slot={lastBlockSlot}; last Haskell Tip slot={lastTipSlot} (outside the range, not an oracle for its final block).");
    }

    // Regression guard: passes on baseline 7521d26 by design; preserves opaque-body decoding and borrowed bytes.
    [Fact]
    public async Task DeepValidBlockBodyIsStillOpaqueAndByteIdentical()
    {
        byte[] body = [.. Enumerable.Repeat((byte)0x9f, 10000), 0x00, .. Enumerable.Repeat((byte)0xff, 10000)];
        byte[] message = CborSerializer.Serialize(new BlockBody(4, new CborEncodedValue(body)));
        using ProtocolReplay replay = new(ProtocolType.BlockFetch, message[..5], message[5..]);
        ChannelBuffer buffer = new(replay.Channel);
        using CancellationTokenSource deadline = new(TimeSpan.FromSeconds(5));
        BlockBody decoded = Assert.IsType<BlockBody>(await buffer.ReceiveFullMessageAsync<BlockFetchMessage>(deadline.Token));
        Assert.Equal(body, decoded.Body.Value.ToArray());
        Assert.Equal(message, decoded.Raw.ToArray());
    }

    [Fact]
    public async Task MalformedDeepIndefiniteItemFailsWithoutRecursion()
    {
        // One million levels distinguishes the iterative check from the baseline recursive walk.
        const int depth = 1_000_000;
        byte[] message = new byte[depth + 1];
        message.AsSpan(0, depth).Fill(0x9f);
        message[depth] = 0x1c;
        using ProtocolReplay replay = new(ProtocolType.BlockFetch, message);
        ChannelBuffer buffer = new(replay.Channel);
        using CancellationTokenSource deadline = new(TimeSpan.FromSeconds(5));
        Exception? error = await Record.ExceptionAsync(() => buffer.ReceiveFullMessageAsync<BlockFetchMessage>(deadline.Token));
        _ = Assert.IsType<CborException>(error);
    }

    [Theory]
    [InlineData("1C")]
    [InlineData("5BFFFFFFFFFFFFFFFF")]
    [InlineData("5A80000000")]
    [InlineData("825A7FFFFFF9")]
    [InlineData("9F1C")]
    [InlineData("BF00FF")]
    [InlineData("5F6161FF")]
    [InlineData("5F5FFFFF")]
    public async Task MalformedOrUnrepresentableItemsFailWithoutWaiting(string hex)
    {
        using ProtocolReplay replay = new(ProtocolType.BlockFetch, Convert.FromHexString(hex));
        ChannelBuffer buffer = new(replay.Channel);
        using CancellationTokenSource deadline = new(TimeSpan.FromSeconds(1));
        Exception? error = await Record.ExceptionAsync(() => buffer.ReceiveFullMessageAsync<BlockFetchMessage>(deadline.Token));
        _ = Assert.IsType<CborException>(error);
    }

    // Regression guards: both rows already fail fast on baseline 7521d26 by design.
    [Theory]
    [InlineData("FF")] // Invalid standalone break, already rejected immediately on the baseline.
    [InlineData("820400")] // Well-formed CBOR with a semantic byte-string-field error, not malformed CBOR.
    public async Task BaselineRejectedMalformedAndSemanticInputsStillFailFast(string hex)
    {
        using ProtocolReplay replay = new(ProtocolType.BlockFetch, Convert.FromHexString(hex));
        ChannelBuffer buffer = new(replay.Channel);
        using CancellationTokenSource deadline = new(TimeSpan.FromSeconds(1));
        Exception? error = await Record.ExceptionAsync(() => buffer.ReceiveFullMessageAsync<BlockFetchMessage>(deadline.Token));
        _ = Assert.IsType<CborException>(error);
    }

    // Regression guard: passes on baseline 7521d26 by design; preserves the existing complete-frame semantic error.
    [Fact]
    public async Task CompleteUndecodableFramePropagatesTheExistingSemanticError()
    {
        byte[] bytes = [0x81, 0x06]; // Well-formed CBOR, but no such BlockFetch message index.
        Exception? expected = Record.Exception(() => CborSerializer.Deserialize<BlockFetchMessage>(bytes, out _));
        Assert.NotNull(expected);
        using ProtocolReplay replay = new(ProtocolType.BlockFetch, bytes);
        ChannelBuffer buffer = new(replay.Channel);
        using CancellationTokenSource deadline = new(TimeSpan.FromSeconds(1));
        Exception? error = await Record.ExceptionAsync(() => buffer.ReceiveFullMessageAsync<BlockFetchMessage>(deadline.Token));
        Assert.NotNull(error);
        Assert.Equal(expected.GetType(), error.GetType());
        Assert.Equal(expected.Message, error.Message);
    }

    // Regression guard: passes on baseline 7521d26 by design; preserves already-demuxed channel EOF behavior.
    [Fact]
    public async Task CompletedProtocolChannelMidBodyFailsClearly()
    {
        using ProtocolReplay replay = new(ProtocolType.BlockFetch, [0x82, 0x04, 0xd8, 0x18, 0x59, 0x01, 0x00, 0x81]);
        // This completes the already-demuxed protocol channel, not a truncated mux transport.
        // The pre-existing Demuxer transport-EOF defect is retained separately as phase-2 evidence.
        replay.CompleteIncoming();
        ChannelBuffer buffer = new(replay.Channel);
        using CancellationTokenSource deadline = new(TimeSpan.FromSeconds(1));
        Exception? error = await Record.ExceptionAsync(() => buffer.ReceiveFullMessageAsync<BlockFetchMessage>(deadline.Token));
        _ = Assert.IsType<ChannelClosedException>(error);
    }

    [Theory]
    [InlineData("8204D81859010081")]
    [InlineData("9F5F4101")]
    public void CompletenessCheckAllocatesNothingOnPartialDefiniteAndIndefiniteItems(string hex)
    {
        // Bind once, outside the allocation window, to the actual receive-path boundary check.
        CompletenessProbe probe = typeof(ChannelBuffer).GetMethod("IsCompleteFrame", BindingFlags.NonPublic | BindingFlags.Static)!
            .CreateDelegate<CompletenessProbe>();
        byte[] bytes = Convert.FromHexString(hex);
        for (int i = 0; i < 100; i++) { Assert.False(probe(bytes)); }
        long before = GC.GetAllocatedBytesForCurrentThread();
        bool complete = false;
        for (int i = 0; i < 1000; i++) { complete |= probe(bytes); }
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.False(complete);
        Assert.Equal(0, allocated);
    }

    [Fact]
    public async Task RecordedExtractPointsAllocateAtMost256BytesPerHeader()
    {
        Recording recording = Recording.Load();
        using ProtocolReplay replay = new(ProtocolType.NodeChainSync, recording.ChainSegments);
        ChannelBuffer buffer = new(replay.Channel);
        using CancellationTokenSource deadline = new(TimeSpan.FromSeconds(30));
        ChainSyncHeader[] headers = new ChainSyncHeader[recording.Digests.Length];
        int count = 0;
        while (count < headers.Length)
        {
            MessageNextResponse next = await buffer.ReceiveFullMessageAsync<MessageNextResponse>(deadline.Token);
            if (next is N2NMessageRollForward forward)
            {
                headers[count++] = ChainSyncHeader.Decode(forward.Payload.Raw);
            }
        }
        ChainPoint[] sink = new ChainPoint[headers.Length];
        for (int warmup = 0; warmup < 10; warmup++) { ExtractPoints(headers, sink); }
        long before = GC.GetAllocatedBytesForCurrentThread();
        ExtractPoints(headers, sink);
        long bytes = GC.GetAllocatedBytesForCurrentThread() - before;
        GC.KeepAlive(sink);
        output.WriteLine($"Warm ExtractPoint allocated {bytes} B for {headers.Length} headers ({(double)bytes / headers.Length} B/header).");
        Assert.True(bytes <= 256L * headers.Length, $"Expected <=256 B/header; got {(double)bytes / headers.Length}.");
    }

    private static void ExtractPoints(ChainSyncHeader[] headers, ChainPoint[] sink)
    {
        for (int index = 0; index < headers.Length; index++) { sink[index] = headers[index].ExtractPoint(); }
    }

    private static (ulong Height, ulong Slot) ReadHeaderNumbers(ReadOnlyMemory<byte> bytes)
    {
        CborReader reader = new(bytes.Span);
        reader.ReadBeginArray();
        _ = reader.ReadSize();
        reader.ReadBeginArray();
        _ = reader.ReadSize();
        return (reader.ReadUInt64(), reader.ReadUInt64());
    }

    // Facts decoded independently with Python cbor2 from the SHA-pinned Haskell-node
    // recording (not computed header hashes). These live Tips are beyond this range.
    private static void AssertNodeTip(Cbor.Common.Tip tip)
    {
        Cbor.Common.SpecificPoint point = Assert.IsType<Cbor.Common.SpecificPoint>(tip.Slot);
        (string hash, int height) = point.Slot switch
        {
            139100417 => ("4473e46f5de089cb04c304ac80846c2af79270dbc19993b1266fe337e60ccd99", 5417032),
            139100422 => ("0d2718d18af42719f633b2018eda2e0b0182d51481aeda5f49e95c130a61fd17", 5417034),
            139100438 => ("d18e35c3e2e7288311c168dca88a3db23d86d76408c3f5eeb15496fe7f737d05", 5417036),
            139100496 => ("202b67ee2168329267bd45a81c541b77aae8121f2bb7d6332a4762ee71d62ee2", 5417038),
            139100556 => ("8a03c7e8047f499e081a74e58d8bb55b14e0ffc9f79cbfc90c8be8a2a2876e5f", 5417039),
            139100566 => ("9199b45b7485f1153afcfa6435345f88ce8d563d6eec5a3081427c1e4044a8ee", 5417040),
            139100599 => ("8390e36f9995023b9dce2d5653fc6bf6a040fc0042c5a61b7fc5b0f769d11582", 5417041),
            139100617 => ("84bf739610c04baf23398e1f964cc9bd5909282622412d9a5f000fda961d3e67", 5417042),
            _ => throw new InvalidOperationException("Unexpected Tip in the recorded range.")
        };
        Assert.Equal(hash, Convert.ToHexStringLower(point.Hash.Span));
        Assert.Equal(height, tip.BlockNumber);
    }

    private static void AssertRecordedIntersection(byte[] bytes)
    {
        MessageIntersectFound intersection = CborSerializer.Deserialize<MessageIntersectFound>(bytes);
        Cbor.Common.SpecificPoint anchor = Assert.IsType<Cbor.Common.SpecificPoint>(intersection.Point);
        Assert.Equal(139055739UL, anchor.Slot);
        Assert.Equal("e5c46f069110d4f8d00a126fd06a783c0eb0a72fe4b9b60be3b3701b1d0b538b", Convert.ToHexStringLower(anchor.Hash.Span));
        AssertNodeTip(intersection.Tip);
    }

    private sealed record Recording(byte[][] FetchSegments, byte[][] ChainSegments, string[] Digests)
    {
        internal static Recording Load()
        {
            string? root = Environment.GetEnvironmentVariable("CHRYSALIS_BLOCKFETCH_RECORDING");
            if (!string.IsNullOrEmpty(root))
            {
                using JsonDocument manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "capture.json")));
                using JsonDocument hashes = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "sha256.json")));
                foreach (string name in new[] { "capture.json", "frames.json", "server-to-client.bin" })
                {
                    Assert.Equal(hashes.RootElement.GetProperty(name).GetString(),
                        Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(Path.Combine(root, name)))));
                }
                using JsonDocument frames = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "frames.json")));
                byte[] wire = File.ReadAllBytes(Path.Combine(root, "server-to-client.bin"));
                byte[][] Segments(int protocol) => [.. frames.RootElement.EnumerateArray()
                    .Where(frame => frame.GetProperty("direction").GetString() == "server-to-client" && frame.GetProperty("protocol").GetInt32() == protocol)
                    .Select(frame => wire.AsSpan(frame.GetProperty("offset").GetInt32() + 8, frame.GetProperty("payloadLength").GetInt32()).ToArray())];
                string[] digests = [.. manifest.RootElement.GetProperty("blocks").EnumerateArray().Select(block => block.GetProperty("sha256").GetString()!)];
                Assert.Equal(1000, digests.Length);
                byte[][] chain = Segments(2);
                AssertRecordedIntersection(chain[0]);
                return new(Segments(3), [.. chain.Skip(1)], digests);
            }
            using JsonDocument fixture = JsonDocument.Parse(PreprodFixtures.Read("BlockFetchRx1/manifest.json"));
            byte[][] Read(int protocol) => [.. fixture.RootElement.GetProperty("segments").EnumerateArray()
                .Where(segment => segment.GetProperty("protocol").GetInt32() == protocol).Select(segment =>
                {
                    byte[] bytes = PreprodFixtures.Read("BlockFetchRx1/" + segment.GetProperty("file").GetString());
                    Assert.Equal(segment.GetProperty("sha256").GetString(), Convert.ToHexStringLower(SHA256.HashData(bytes)));
                    return bytes;
                })];
            byte[][] fixtureChain = Read(2);
            AssertRecordedIntersection(fixtureChain[0]);
            return new(Read(3), [.. fixtureChain.Skip(1)], [.. fixture.RootElement.GetProperty("blocks").EnumerateArray().Select(block => block.GetProperty("sha256").GetString()!)]);
        }
    }
}
