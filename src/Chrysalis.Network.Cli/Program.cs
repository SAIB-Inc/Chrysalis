

using System.Diagnostics;
using Chrysalis.Cbor.Cardano.Extensions;
using Chrysalis.Cbor.Cardano.Types.Block;
using Chrysalis.Network.Cbor.ChainSync;
using Chrysalis.Network.Cbor.Common;
using Chrysalis.Network.Cbor.Handshake;
using Chrysalis.Network.Cli;
using Chrysalis.Network.Multiplexer;

// home/rjlacanlale/cardano/ipc/node.socket
//NodeClient client = await NodeClient.ConnectAsync("/tmp/intercept_node_socket");
NodeClient client = await NodeClient.ConnectAsync("/home/rjlacanlale/cardano/ipc/node.socket");
client.Start();

ProposeVersions proposeVersion = HandshakeMessages.ProposeVersions(VersionTables.N2C_V10_AND_ABOVE);

Console.WriteLine("Sending handshake message...");
await client.Handshake!.SendAsync(proposeVersion, CancellationToken.None);
Console.WriteLine("Handshake success!!");

// Point point = new(
//     new(73793022),
//     new(Convert.FromHexString("1b6b4afeef73bf4a1e2a014b492549b6cedc61919fda6a7e4d3813f578eba4db")));
Point point = new(
    new(57371845),
    new(Convert.FromHexString("20a81db38339bf6ee9b1d7e22b22c0ac4d887d332bbf4f3005db4848cd647743")));

Console.WriteLine("Finding Intersection...");
await client.ChainSync!.FindIntersectionAsync([point], CancellationToken.None);
Console.WriteLine("Intersection found");

Console.WriteLine("Starting ChainSync...");
int blockCount = 0;
_ = Task.Run(async () =>
{
    while (true)
    {
        Console.WriteLine($"Block count: {blockCount}");
        blockCount = 0;
        await Task.Delay(1000);
    }
});

while (true)
{
    try
    {
        MessageNextResponse nextResponse = await client.ChainSync!.NextRequestAsync(CancellationToken.None);

        switch (nextResponse)
        {
            case MessageRollBackward msg:
                Console.WriteLine($"Rolling back to {msg.Point.Slot}");
                break;
            case MessageRollForward msg:
                blockCount++;
                // Block? block = TestUtils.DeserializeBlockWithEra(msg.Payload.Value);
                //Console.WriteLine($"Rolling forward to block: {block!.Slot()} # {block!.Hash()}");
                break;
            case MessageAwaitReply msg:
                Console.WriteLine($"Block count: {blockCount}");
                blockCount = 0;
                Console.WriteLine("Tip reached!!!");
                break;
            default:
                // Console.WriteLine("ss");
                break;
        }
    }
    catch
    {
        //
    }
}


// async Task Main()
// {
//     // For tracking blocks processed per second
//     Stopwatch secondTimer = Stopwatch.StartNew();
//     Stopwatch totalTimer = Stopwatch.StartNew();
//     int blocksProcessed = 0;
//     int totalBlocksProcessed = 0;
//     ulong lastBlockNo = 0;

//     // Idle time tracking
//     bool isAtTip = false;
//     Stopwatch idleTimer = new Stopwatch();
//     TimeSpan totalIdleTime = TimeSpan.Zero;

//     // Initial sync to tip tracking
//     Stopwatch syncToTipTimer = Stopwatch.StartNew();
//     bool firstTipReached = false;
//     int blocksToFirstTip = 0;

//     // Set up Ctrl+C handler to show stats when exiting
//     Console.CancelKeyPress += (sender, e) =>
//     {
//         // Capture final idle time if we're at the tip
//         if (isAtTip && idleTimer.IsRunning)
//         {
//             totalIdleTime += idleTimer.Elapsed;
//         }

//         double activeTime = totalTimer.Elapsed.TotalSeconds - totalIdleTime.TotalSeconds;
//         Console.WriteLine("\n--- Sync Summary ---");
//         Console.WriteLine($"Time to sync to first tip: {syncToTipTimer.Elapsed.TotalSeconds:F2} seconds");
//         Console.WriteLine($"Blocks processed to first tip: {blocksToFirstTip}");
//         Console.WriteLine($"Average rate to first tip: {blocksToFirstTip / syncToTipTimer.Elapsed.TotalSeconds:F2} blocks/sec");
//         Console.WriteLine($"Total blocks processed: {totalBlocksProcessed}");
//         Console.WriteLine($"Total time: {totalTimer.Elapsed.TotalSeconds:F2} seconds");
//         Console.WriteLine($"Active sync time: {activeTime:F2} seconds");
//         Console.WriteLine($"Idle time at tip: {totalIdleTime.TotalSeconds:F2} seconds");
//         Console.WriteLine($"Overall average rate: {totalBlocksProcessed / activeTime:F2} blocks/sec");
//     };

//     // Connect to node
//     Console.WriteLine("Connecting to node...");
// NodeClient client = await NodeClient.ConnectAsync("/home/rjlacanlale/cardano/ipc/node.socket");
// client.Start();

//     UnixBearer bearer = await UnixBearer.CreateAsync("/home/rjlacanlale/cardano/ipc/node.socket");
//     Plexer plexer = new(bearer);

//     // Handshake
//     // Console.WriteLine("Performing handshake...");
// var handshakeResult = await client.Handshake!.SendAsync(
//     HandshakeMessages.ProposeVersions(VersionTables.N2C_V10_AND_ABOVE),
//     CancellationToken.None);

//     // 000005fa000000218200a51980108202f41980118202f41980128202f41980138202f41980148202f4

//     // STEP1: Handshake
//     HandshakeMessage handshakeMessage = await ReadWriteSegmentAsync<HandshakeMessage>(
//         new(Convert.FromHexString("000005fa000000218200a51980108202f41980118202f41980128202f41980138202f41980148202f4")
//     ), plexer);

//     Console.WriteLine(handshakeMessage);

//     // Find intersection
//     Console.WriteLine("Finding intersection point...");
// var point = new Point(
//     new(73793022),
//     new(Convert.FromHexString("1b6b4afeef73bf4a1e2a014b492549b6cedc61919fda6a7e4d3813f578eba4db")));

//     // 0000a3ba0005002b820481821a0465fdfe58201b6b4afeef73bf4a1e2a014b492549b6cedc61919fda6a7e4d3813f578eba4db
// await client.ChainSync!.FindIntersectionAsync([point], CancellationToken.None);
//     // await client.ChainSync!.Channel.EnqueueChunkAsync(new(Convert.FromHexString("0000a3ba0005002b820481821a0465fdfe58201b6b4afeef73bf4a1e2a014b492549b6cedc61919fda6a7e4d3813f578eba4db")), CancellationToken.None);
//     // await client.ChainSync!.Channel.ReadChunkAsync(CancellationToken.None);

// ChainSyncMessage findIntersectionMessage = await ReadWriteSegmentAsync<ChainSyncMessage>(
//     new(Convert.FromHexString("0000a3ba0005002b820481821a0465fdfe58201b6b4afeef73bf4a1e2a014b492549b6cedc61919fda6a7e4d3813f578eba4db")
// ), plexer);

//     Console.WriteLine(findIntersectionMessage);

//     Console.WriteLine("Starting chain sync...");
//     while (true)
//     {
//         //000185ea000500028100
//         //MessageNextResponse? nextResponse = await client.ChainSync!.NextRequestAsync(CancellationToken.None);
//         // await client.ChainSync!.Channel.EnqueueChunkAsync(new(Convert.FromHexString("000185ea000500028100")), CancellationToken.None);
//         // var nextResponse = await client.ChainSync!.Channel.ReadChunkAsync(CancellationToken.None);

//         try
//         {
//             MessageNextResponse nextResponseMessage = await ReadWriteSegmentAsync<MessageNextResponse>(new(Convert.FromHexString("000185ea000500028100")), plexer);
//             BlockWithEra<ConwayBlock> block = CborSerializer.Deserialize<BlockWithEra<ConwayBlock>>(((MessageRollForward)nextResponseMessage).Payload.Value);

//             (bool shouldLog, ulong blockNo, bool isNewBlock, bool currentAtTip) = ProcessNextResponseRaw(
//            //nextResponse,
//            ref blocksProcessed,
//            ref lastBlockNo,
//            secondTimer
//        );

//             // Handle tip state changes and idle time tracking
//             if (currentAtTip && !isAtTip)
//             {
//                 // Just reached the tip
//                 isAtTip = true;
//                 if (!firstTipReached)
//                 {
//                     syncToTipTimer.Stop();
//                     firstTipReached = true;
//                     Console.WriteLine($"Reached tip after processing {blocksToFirstTip} blocks in {syncToTipTimer.Elapsed.TotalSeconds:F2} seconds");
//                 }
//                 idleTimer.Start();
//                 Console.WriteLine("Entering idle state at tip");
//             }
//             else if (!currentAtTip && isAtTip)
//             {
//                 // No longer at tip (new blocks arrived)
//                 isAtTip = false;
//                 idleTimer.Stop();
//                 totalIdleTime += idleTimer.Elapsed;
//                 idleTimer.Reset();
//                 Console.WriteLine($"Exiting idle state, idle time so far: {totalIdleTime.TotalSeconds:F2}s");
//             }

//             // Update block counts
//             if (isNewBlock)
//             {
//                 if (!firstTipReached)
//                 {
//                     blocksToFirstTip++;
//                 }
//                 totalBlocksProcessed++;

//                 // Log immediately for blocks after tip
//                 if (isAtTip)
//                 {
//                     Console.WriteLine($"New block arrived while at tip: {blockNo}");
//                     shouldLog = true;
//                 }
//             }

//             // Log on timer or when explicitly requested
//             if (shouldLog)
//             {
//                 Console.WriteLine($"Processed {blocksProcessed} blocks in the last {secondTimer.ElapsedMilliseconds}ms. " +
//                                   $"Latest block: {lastBlockNo} | Total: {totalBlocksProcessed} blocks in " +
//                                   $"{totalTimer.Elapsed.TotalSeconds - totalIdleTime.TotalSeconds:F2}s active time");
//                 blocksProcessed = 0;
//                 secondTimer.Restart();
//             }

//         }
//         catch
//         {

//         }
//     }
// }

// // static async Task ReadWriteSegmentAsync<T>(ReadOnlySequence<byte> segment, UnixBearer bearer) where T : CborBase
// // {
// //     await bearer.Writer.WriteAsync(segment.First, CancellationToken.None);
// //     ReadResult headerResult = await bearer.Reader.ReadAtLeastAsync(8, CancellationToken.None);
// //     ReadOnlySequence<byte> headerSlice = headerResult.Buffer.Slice(0, 8);
// //     MuxSegmentHeader headerSegment = MuxSegmentCodec.DecodeHeader(headerSlice);
// //     bearer.Reader.AdvanceTo(headerResult.Buffer.GetPosition(8));

// //     ReadResult payloadResult = await bearer.Reader.ReadAtLeastAsync(headerSegment.PayloadLength);
// //     ReadOnlySequence<byte> payloadSlice = payloadResult.Buffer.Slice(0, headerSegment.PayloadLength);

// //     try
// //     {
// //         MessageNextResponse nextResponse = CborSerializer.Deserialize<MessageNextResponse>(payloadSlice.First);
// //         // CborSerializer.Deserialize<BlockWithEra<ConwayBlock>>(((MessageRollForward)nextResponse).Payload.Value);
// //     }
// //     catch
// //     {
// //         //
// //     }

// //     bearer.Reader.AdvanceTo(payloadResult.Buffer.GetPosition(headerSegment.PayloadLength));

// //     // return new MuxSegment(headerSegment, payloadSlice);
// // }

// // static async Task<M> ReadWriteSegmentAsync<M>(ReadOnlySequence<byte> segment, Plexer plexer) where M : CborBase
// // {
// //     await plexer._muxer.WriteSegmentTestAsync(segment, CancellationToken.None);
// //     MuxSegment messageSegment = await plexer._demuxer.ReadSegmentAsync(CancellationToken.None);

// //     M message = CborSerializer.Deserialize<M>(messageSegment.Payload.First);
// //     return message;
// // }

// // (bool shouldLog, ulong blockNo, bool isNewBlock, bool atTip) ProcessNextResponse(
// //     MessageNextResponse nextResponse,
// //     ref int blocksProcessed,
// //     ref ulong lastBlockNo,
// //     Stopwatch timer)
// // {
// //     ulong blockNo = 0;
// //     bool isNewBlock = false;
// //     bool atTip = false;

// //     switch (nextResponse)
// //     {
// //         case MessageRollForward messageRollForward:
// //             // Got a new block
// //             Block? block = TestUtils.DeserializeBlockWithEra(messageRollForward.Payload.Value)!;
// //             blockNo = block.Number()!.Value;
// //             blocksProcessed++;
// //             lastBlockNo = blockNo;
// //             isNewBlock = true;

// //             // Output basic block info
// //             //Console.WriteLine($"Block: {blockNo} | Hash: {block.Block.Hash()} | Slot: {block.Block.Slot()}");

// //             // We received a block, so we're definitely not at tip
// //             atTip = false;
// //             break;

// //         case MessageRollBackward messageRollBackward:
// //             blockNo = messageRollBackward.Point.Slot.Value;
// //             //Console.WriteLine($"ROLLBACK to slot {blockNo}");
// //             // After rollback, we're not at tip
// //             atTip = false;
// //             break;

// //         default: // MessageAwaitReply
// //             Console.WriteLine("Tip Reached! Waiting for next block...");
// //             timer.Restart();
// //             // This is the signal we're at tip
// //             atTip = true;
// //             // Reset blocksProcessed when reaching the tip to avoid duplicate counting
// //             blocksProcessed = 0;
// //             break;
// //     }

// //     return (timer.ElapsedMilliseconds >= 1000, blockNo, isNewBlock, atTip);
// // }

// (bool shouldLog, ulong blockNo, bool isNewBlock, bool atTip) ProcessNextResponseRaw(
//     ref int blocksProcessed,
//     ref ulong lastBlockNo,
//     Stopwatch timer)
// {
//     ulong blockNo = 0;
//     bool isNewBlock = false;
//     bool atTip = false;

//     blocksProcessed++;
//     lastBlockNo = blockNo;
//     isNewBlock = true;

//     blocksProcessed++;
//     lastBlockNo = blockNo;
//     isNewBlock = true;

//     // Output basic block info
//     //Console.WriteLine($"Block: {blockNo} | Hash: {block.Block.Hash()} | Slot: {block.Block.Slot()}");

//     // We received a block, so we're definitely not at tip
//     atTip = false;

//     return (timer.ElapsedMilliseconds >= 1000, blockNo, isNewBlock, atTip);
// }

// await Main();