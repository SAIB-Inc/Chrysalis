using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using Chrysalis.Codec.Extensions.Cardano.Core;
using Chrysalis.Codec.Serialization;
using Chrysalis.Codec.Types.Cardano.Core;
using Chrysalis.Network.Cbor.ChainSync;
using Chrysalis.Network.Cbor.Common;
using Chrysalis.Network.MiniProtocols;
using Chrysalis.Network.Multiplexer;

namespace Chrysalis.Benchmarks;

internal sealed record RangePoint(ulong Slot, string Hash, string Sha256)
{
    internal Point ToPoint() => Point.Specific(Slot, Convert.FromHexString(Hash));
}

internal sealed record FixedRange(RangePoint Anchor, RangePoint[] Blocks);

internal static class WholePath
{
    internal static async Task CaptureAsync(string[] args)
    {
        if (args.Length != 3) { throw new ArgumentException("capture-range requires SOCKET COUNT OUTPUT.", nameof(args)); }
        int count = int.Parse(args[1], CultureInfo.InvariantCulture);
        ArgumentOutOfRangeException.ThrowIfLessThan(count, 1000);
        using JsonDocument manifest = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(Fixtures.Root, "fixtures/manifest.json")).ConfigureAwait(false));
        JsonElement first = manifest.RootElement[0];
        RangePoint anchor = new(first.GetProperty("slot").GetUInt64(), first.GetProperty("hash").GetString()!, "");
        using CancellationTokenSource cancellation = new(TimeSpan.FromMinutes(10));
        using NodeClient client = await NodeClient.ConnectAsync(args[0], cancellation.Token).ConfigureAwait(false);
        await client.StartAsync(1).WaitAsync(TimeSpan.FromSeconds(30), cancellation.Token).ConfigureAwait(false);
        await IntersectAsync(client.ChainSync, anchor, cancellation.Token).ConfigureAwait(false);
        RangePoint[] points = new RangePoint[count];
        int index = 0;
        bool rolledBack = false;
        while (index < points.Length)
        {
            MessageNextResponse? message = await client.ChainSync.NextRequestAsync(cancellation.Token).ConfigureAwait(false);
            if (message is N2CMessageRollForward forward)
            {
                // ChannelBuffer's lazy views are valid only until the next receive.
                BlockWithEra block = CborSerializer.Deserialize<BlockWithEra>(forward.Payload.Value);
                points[index++] = new(block.Block.Slot(), block.Block.Header().Hash(), Convert.ToHexStringLower(SHA256.HashData(block.Raw.Span)));
            }
            else if (message is MessageRollBackward && index == 0 && !rolledBack) { rolledBack = true; }
            else { throw new InvalidDataException("Unexpected capture response: " + message?.GetType().Name); }
        }
        await File.WriteAllTextAsync(args[2], JsonSerializer.Serialize(new FixedRange(anchor, points), Program.IndentedJson)).ConfigureAwait(false);
        await client.ChainSync.DoneAsync(cancellation.Token).ConfigureAwait(false);
    }

    internal static async Task RunAsync(string[] args)
    {
        if (args.Length != 3) { throw new ArgumentException("network requires n2c|n2n ENDPOINT RANGE_JSON.", nameof(args)); }
        FixedRange range = JsonSerializer.Deserialize<FixedRange>(await File.ReadAllTextAsync(args[2]).ConfigureAwait(false)) ?? throw new InvalidDataException("Missing fixed range.");
        if (range.Blocks.Length < 1000) { throw new InvalidDataException("Whole-path range must contain at least 1,000 blocks."); }
        byte[][] digests = [.. range.Blocks.Select(point => Convert.FromHexString(point.Sha256))];
        using CancellationTokenSource cancellation = new(TimeSpan.FromMinutes(10));
        if (args[0] == "n2c")
        {
            using NodeClient client = await NodeClient.ConnectAsync(args[1], cancellation.Token).ConfigureAwait(false);
            await client.StartAsync(1).WaitAsync(TimeSpan.FromSeconds(30), cancellation.Token).ConfigureAwait(false);
            await IntersectAsync(client.ChainSync, range.Anchor, cancellation.Token).ConfigureAwait(false);
            _ = await ReadBlocksAsync(client.ChainSync, digests, cancellation.Token).ConfigureAwait(false);
            await IntersectAsync(client.ChainSync, range.Anchor, cancellation.Token).ConfigureAwait(false);
            Stopwatch timer = new();
            GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
            long before = GC.GetTotalAllocatedBytes(true);
            timer.Start();
            long wireBytes = await ReadBlocksAsync(client.ChainSync, digests, cancellation.Token).ConfigureAwait(false);
            timer.Stop();
            long allocated = GC.GetTotalAllocatedBytes(true) - before;
            WriteResult("N2C_full_blocks", timer, allocated, wireBytes, range);
            await client.ChainSync.DoneAsync(cancellation.Token).ConfigureAwait(false);
        }
        else if (args[0] == "n2n")
        {
            using PeerClient client = await PeerClient.ConnectAsync(args[1], 3001, cancellation.Token).ConfigureAwait(false);
            await client.StartAsync(1).WaitAsync(TimeSpan.FromSeconds(30), cancellation.Token).ConfigureAwait(false);
            ChainPoint[] points = new ChainPoint[range.Blocks.Length];
            await IntersectAsync(client.ChainSync, range.Anchor, cancellation.Token).ConfigureAwait(false);
            _ = await ReadHeadersAndFetchAsync(client, points, range, digests, cancellation.Token).ConfigureAwait(false);
            ValidatePoints(points, range);
            Array.Clear(points);
            await IntersectAsync(client.ChainSync, range.Anchor, cancellation.Token).ConfigureAwait(false);
            Stopwatch timer = new();
            GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
            long before = GC.GetTotalAllocatedBytes(true);
            timer.Start();
            long wireBytes = await ReadHeadersAndFetchAsync(client, points, range, digests, cancellation.Token).ConfigureAwait(false);
            timer.Stop();
            long allocated = GC.GetTotalAllocatedBytes(true) - before;
            ValidatePoints(points, range);
            WriteResult("N2N_headers_and_block_fetch", timer, allocated, wireBytes, range);
            await client.ChainSync.DoneAsync(cancellation.Token).ConfigureAwait(false);
            await client.BlockFetch.DoneAsync(cancellation.Token).ConfigureAwait(false);
        }
        else { throw new ArgumentException("Protocol must be n2c or n2n.", nameof(args)); }
    }

    private static async Task IntersectAsync(ChainSync chain, RangePoint anchor, CancellationToken token)
    {
        if (await chain.FindIntersectionAsync([anchor.ToPoint()], token).ConfigureAwait(false) is not MessageIntersectFound)
        {
            throw new InvalidDataException("Fixed range intersection is no longer on the node's chain.");
        }
    }

    private static async Task<long> ReadBlocksAsync(ChainSync chain, byte[][] digests, CancellationToken token)
    {
        int index = 0;
        long bytes = 0;
        bool rolledBack = false;
        while (index < digests.Length)
        {
            MessageNextResponse? message = await chain.NextRequestAsync(token).ConfigureAwait(false);
            if (message is N2CMessageRollForward forward)
            {
                BlockWithEra block = CborSerializer.Deserialize<BlockWithEra>(forward.Payload.Value);
                ValidateBorrowedBlock(block.Raw, digests[index++]);
                bytes += block.Raw.Length;
            }
            else if (message is MessageRollBackward && index == 0 && !rolledBack) { rolledBack = true; }
            else { throw new InvalidDataException("Unexpected fixed-work N2C response: " + message?.GetType().Name); }
        }
        return bytes;
    }

    private static async Task<long> ReadHeadersAndFetchAsync(PeerClient client, ChainPoint[] points,
        FixedRange range, byte[][] digests, CancellationToken token)
    {
        int index = 0;
        bool rolledBack = false;
        while (index < points.Length)
        {
            MessageNextResponse? message = await client.ChainSync.NextRequestAsync(token).ConfigureAwait(false);
            if (message is N2NMessageRollForward forward)
            {
                points[index++] = ChainSyncHeader.Decode(forward.Payload.Raw).ExtractPoint();
            }
            else if (message is MessageRollBackward && index == 0 && !rolledBack) { rolledBack = true; }
            else { throw new InvalidDataException("Unexpected fixed-work N2N response: " + message?.GetType().Name); }
        }
        index = 0;
        long bytes = 0;
        await foreach (BlockWithEra block in client.BlockFetch.FetchRangeAsync<BlockWithEra>(range.Blocks[0].ToPoint(), range.Blocks[^1].ToPoint(), token).ConfigureAwait(false))
        {
            if (index >= digests.Length) { throw new InvalidDataException("BlockFetch returned too many blocks."); }
            ValidateBorrowedBlock(block.Raw, digests[index++]);
            bytes += block.Raw.Length;
        }
        if (index != digests.Length) { throw new InvalidDataException("BlockFetch returned too few blocks."); }
        return bytes;
    }

    internal static void ValidateBorrowedBlock(ReadOnlyMemory<byte> bytes, byte[] expected)
    {
        // This is deliberately a synchronous leaf: stack scratch never crosses an await,
        // and borrowed SDK data is consumed before the next receive can reuse storage.
        Span<byte> digest = stackalloc byte[32];
        _ = SHA256.HashData(bytes.Span, digest);
        if (!digest.SequenceEqual(expected)) { throw new InvalidDataException("Changed block bytes in fixed range."); }
    }

    private static void ValidatePoints(ChainPoint[] points, FixedRange range)
    {
        for (int index = 0; index < points.Length; index++)
        {
            if (points[index].Slot != range.Blocks[index].Slot || !points[index].Hash.AsSpan().SequenceEqual(Convert.FromHexString(range.Blocks[index].Hash)))
            {
                throw new InvalidDataException("N2N headers changed in fixed range.");
            }
        }
    }

    private static void WriteResult(string name, Stopwatch timer, long allocated, long wireBytes, FixedRange range)
    {
        object result = new
        {
            name,
            runtime = Environment.Version.ToString(),
            environment = Program.EnvironmentSnapshot(),
            blocks = range.Blocks.Length,
            ns = timer.Elapsed.TotalNanoseconds / range.Blocks.Length,
            bytes = allocated / (double)range.Blocks.Length,
            blocksPerSecond = range.Blocks.Length / timer.Elapsed.TotalSeconds,
            wireBytes,
            allocationCounter = "GC.GetTotalAllocatedBytes(true)",
            setupExcluded = true,
            blockDecode = "lazy BlockWithEra; complete block bytes received",
            validation = "per-block SHA256 into stack span included; no borrowed payload retained across receives",
            pipelineDepth = 1,
            anchor = range.Anchor,
            end = range.Blocks[^1]
        };
        _ = Directory.CreateDirectory(Program.ResultsRoot);
        string json = JsonSerializer.Serialize(result, Program.IndentedJson);
        File.WriteAllText(Path.Combine(Program.ResultsRoot, name + ".json"), json);
        Console.WriteLine(json);
    }
}
