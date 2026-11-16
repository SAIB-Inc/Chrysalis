using System.Buffers;
using System.Diagnostics;
using System.IO.Pipelines;
using System.Security.Cryptography;
using System.Text.Json;
using Chrysalis.Codec.Extensions.Cardano.Core;
using Chrysalis.Codec.Serialization;
using Chrysalis.Codec.Types;
using Chrysalis.Codec.Types.Cardano.Core;
using Chrysalis.Network.Cbor.BlockFetch;
using Chrysalis.Network.Cbor.ChainSync;
using Chrysalis.Network.Cbor.Common;
using Chrysalis.Network.Core;
using Chrysalis.Network.Multiplexer;

namespace Chrysalis.Benchmarks;

// Attribution control only: receive-side demux + ChannelBuffer + protocol/block
// decode over an in-memory pipe. No socket, node, handshake or client send path.
internal static class OfflineReplay
{
    private const int Passes = 34;

    internal static async Task RunAsync()
    {
        byte[][] blocks = Fixtures.Load("block-*.cbor");
        byte[][] digests = [.. blocks.Select(SHA256.HashData)];
        byte[][] n2c = new byte[blocks.Length][];
        byte[][] headers = new byte[blocks.Length][];
        byte[][] bodies = new byte[blocks.Length][];
        for (int i = 0; i < blocks.Length; i++)
        {
            BlockWithEra block = CborSerializer.Deserialize<BlockWithEra>(blocks[i]);
            Tip tip = new(Point.Specific(block.Block.Slot(), Convert.FromHexString(block.Block.Header().Hash())), 1);
            n2c[i] = Segment(ProtocolType.ClientChainSync, CborSerializer.Serialize(new N2CMessageRollForward(2, new CborEncodedValue(blocks[i]), tip)));
            N2NBlockHeader header = N2NBlockHeader.Create(6, new ShelleyN2NHeader(new CborEncodedValue(block.Block.Header().Raw)));
            headers[i] = Segment(ProtocolType.NodeChainSync, CborSerializer.Serialize(new N2NMessageRollForward(2, header, tip)));
            bodies[i] = Segment(ProtocolType.BlockFetch, CborSerializer.Serialize(new BlockBody(4, new CborEncodedValue(blocks[i]))));
        }
        List<object> rows = [];
        foreach (bool nodeToNode in new[] { false, true })
        {
            using ReplayBearer bearer = new();
            using Plexer plexer = new(bearer);
            ChannelBuffer chain = new(plexer.SubscribeClient(nodeToNode ? ProtocolType.NodeChainSync : ProtocolType.ClientChainSync));
            ChannelBuffer fetch = new(plexer.SubscribeClient(ProtocolType.BlockFetch));
            using CancellationTokenSource cancellation = new(TimeSpan.FromMinutes(5));
            Task worker = plexer.RunAsync(cancellation.Token);
            try
            {
                Stopwatch warmup = Stopwatch.StartNew();
                do { _ = await ReadPassAsync(nodeToNode, bearer, chain, fetch, n2c, headers, bodies, digests, cancellation.Token).ConfigureAwait(false); }
                while (warmup.Elapsed.TotalSeconds < 1);
                List<object> samples = [];
                Stopwatch timer = new();
                for (int batch = 0; batch < 5; batch++)
                {
                    GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
                    long before = GC.GetTotalAllocatedBytes(true);
                    timer.Restart();
                    long wireBytes = 0;
                    for (int pass = 0; pass < Passes; pass++)
                    {
                        wireBytes += await ReadPassAsync(nodeToNode, bearer, chain, fetch, n2c, headers, bodies, digests, cancellation.Token).ConfigureAwait(false);
                    }
                    timer.Stop();
                    long allocated = GC.GetTotalAllocatedBytes(true) - before;
                    int count = blocks.Length * Passes;
                    samples.Add(new { batch, blocks = count, ns = timer.Elapsed.TotalNanoseconds / count, bytes = allocated / (double)count, wireBytes });
                }
                rows.Add(new
                {
                    name = nodeToNode ? "N2N_receive_replay" : "N2C_receive_replay",
                    samples,
                    method = "GC.GetTotalAllocatedBytes(true); actual demux/channel/protocol/CBOR receive path over a memory pipe",
                    limitation = "NOT live whole-path attribution: excludes socket, request sending, node and BlockFetch agency; includes common pipe producer overhead"
                });
            }
            finally
            {
                cancellation.Cancel();
                try { await worker.ConfigureAwait(false); }
                catch (OperationCanceledException) { }
                catch (InvalidOperationException) { }
            }
        }
        _ = Directory.CreateDirectory(Program.ResultsRoot);
        string json = JsonSerializer.Serialize(new { environment = Program.EnvironmentSnapshot(), rows }, Program.IndentedJson);
        File.WriteAllText(Path.Combine(Program.ResultsRoot, "offline-replay.json"), json);
        Console.WriteLine(json);
    }

    private static byte[] Segment(ProtocolType protocol, byte[] payload) => MuxSegmentCodec.Encode(new MuxSegment(
        new MuxSegmentHeader(0, protocol, checked((ushort)payload.Length), false), new ReadOnlySequence<byte>(payload))).ToArray();

    private static async Task<long> ReadPassAsync(bool nodeToNode, ReplayBearer bearer, ChannelBuffer chain, ChannelBuffer fetch,
        byte[][] n2c, byte[][] headers, byte[][] bodies, byte[][] digests, CancellationToken token)
    {
        long wireBytes = 0;
        for (int i = 0; i < n2c.Length; i++)
        {
            _ = await bearer.Input.WriteAsync(nodeToNode ? headers[i] : n2c[i], token).ConfigureAwait(false);
            MessageNextResponse message = await chain.ReceiveFullMessageAsync<MessageNextResponse>(token).ConfigureAwait(false);
            if (nodeToNode && message is N2NMessageRollForward header)
            {
                ResultSink<ChainPoint>.Value = ChainSyncHeader.Decode(header.Payload.Raw).ExtractPoint();
            }
            else if (!nodeToNode && message is N2CMessageRollForward block)
            {
                BlockWithEra decoded = CborSerializer.Deserialize<BlockWithEra>(block.Payload.Value);
                WholePath.ValidateBorrowedBlock(decoded.Raw, digests[i]);
                wireBytes += decoded.Raw.Length;
            }
            else { throw new InvalidDataException("Unexpected replay chain-sync message."); }
        }
        if (nodeToNode)
        {
            for (int i = 0; i < bodies.Length; i++)
            {
                _ = await bearer.Input.WriteAsync(bodies[i], token).ConfigureAwait(false);
                BlockFetchMessage message = await fetch.ReceiveFullMessageAsync<BlockFetchMessage>(token).ConfigureAwait(false);
                if (message is not BlockBody block) { throw new InvalidDataException("Unexpected replay BlockFetch message."); }
                BlockWithEra decoded = CborSerializer.Deserialize<BlockWithEra>(block.Body.Value);
                WholePath.ValidateBorrowedBlock(decoded.Raw, digests[i]);
                wireBytes += decoded.Raw.Length;
            }
        }
        return wireBytes;
    }

    private sealed class ReplayBearer : IBearer
    {
        private readonly Pipe input = new();
        private readonly Pipe output = new();
        private bool disposed;
        internal PipeWriter Input => input.Writer;
        public PipeReader Reader => input.Reader;
        public PipeWriter Writer => output.Writer;
        public void Dispose()
        {
            if (disposed) { return; }
            disposed = true;
            input.Reader.Complete(); input.Writer.Complete(); output.Reader.Complete(); output.Writer.Complete();
        }
    }
}
