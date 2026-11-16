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
        output.WriteLine($"Validated {recording.Digests.Length} body SHA256s and node-supplied raw headers; {recording.Digests.Length - 1} node-supplied predecessor hashes plus the anchor. Header/body hashes are Chrysalis-computed; the last digest has no following-header oracle. Last block slot={lastBlockSlot}; last Haskell Tip slot={lastTipSlot}.");
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
                return new(Segments(3), [.. Segments(2).Skip(1)], digests);
            }
            using JsonDocument fixture = JsonDocument.Parse(PreprodFixtures.Read("BlockFetchRx1/manifest.json"));
            byte[][] Read(int protocol) => [.. fixture.RootElement.GetProperty("segments").EnumerateArray()
                .Where(segment => segment.GetProperty("protocol").GetInt32() == protocol).Select(segment =>
                {
                    byte[] bytes = PreprodFixtures.Read("BlockFetchRx1/" + segment.GetProperty("file").GetString());
                    Assert.Equal(segment.GetProperty("sha256").GetString(), Convert.ToHexStringLower(SHA256.HashData(bytes)));
                    return bytes;
                })];
            return new(Read(3), [.. Read(2).Skip(1)], [.. fixture.RootElement.GetProperty("blocks").EnumerateArray().Select(block => block.GetProperty("sha256").GetString()!)]);
        }
    }
}
