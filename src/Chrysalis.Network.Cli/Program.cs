using Chrysalis.Cbor.Cardano.Extensions;
using Chrysalis.Cbor.Cardano.Types.Block;
using Chrysalis.Cbor.Serialization;
using Chrysalis.Network.Cbor.ChainSync;
using Chrysalis.Network.Cbor.Common;
using Chrysalis.Network.Cbor.Handshake;
using Chrysalis.Network.Cli;
using Chrysalis.Network.Multiplexer;
using System.Diagnostics;
using System.Formats.Cbor;


async Task Main()
{
    // For tracking blocks processed per second
    Stopwatch secondTimer = Stopwatch.StartNew();
    Stopwatch totalTimer = Stopwatch.StartNew();
    int blocksProcessed = 0;
    int totalBlocksProcessed = 0;
    ulong lastBlockNo = 0;

    // Idle time tracking
    bool isAtTip = false;
    Stopwatch idleTimer = new Stopwatch();
    TimeSpan totalIdleTime = TimeSpan.Zero;

    // Initial sync to tip tracking
    Stopwatch syncToTipTimer = Stopwatch.StartNew();
    bool firstTipReached = false;
    int blocksToFirstTip = 0;

    // Set up Ctrl+C handler to show stats when exiting
    Console.CancelKeyPress += (sender, e) =>
    {
        // Capture final idle time if we're at the tip
        if (isAtTip && idleTimer.IsRunning)
        {
            totalIdleTime += idleTimer.Elapsed;
        }

        double activeTime = totalTimer.Elapsed.TotalSeconds - totalIdleTime.TotalSeconds;
        Console.WriteLine("\n--- Sync Summary ---");
        Console.WriteLine($"Time to sync to first tip: {syncToTipTimer.Elapsed.TotalSeconds:F2} seconds");
        Console.WriteLine($"Blocks processed to first tip: {blocksToFirstTip}");
        Console.WriteLine($"Average rate to first tip: {blocksToFirstTip / syncToTipTimer.Elapsed.TotalSeconds:F2} blocks/sec");
        Console.WriteLine($"Total blocks processed: {totalBlocksProcessed}");
        Console.WriteLine($"Total time: {totalTimer.Elapsed.TotalSeconds:F2} seconds");
        Console.WriteLine($"Active sync time: {activeTime:F2} seconds");
        Console.WriteLine($"Idle time at tip: {totalIdleTime.TotalSeconds:F2} seconds");
        Console.WriteLine($"Overall average rate: {totalBlocksProcessed / activeTime:F2} blocks/sec");
    };

    // Connect to node
    Console.WriteLine("Connecting to node...");
    NodeClient client = await NodeClient.ConnectAsync("/home/rjlacanlale/cardano/ipc/node.socket");
    client.Start();

    // Handshake
    Console.WriteLine("Performing handshake...");
    var handshakeResult = await client.Handshake!.SendAsync(
        HandshakeMessages.ProposeVersions(VersionTables.N2C_V10_AND_ABOVE),
        CancellationToken.None);

    // Find intersection
    Console.WriteLine("Finding intersection point...");
    var point = new Point(
        new(73793022),
        new(Convert.FromHexString("1b6b4afeef73bf4a1e2a014b492549b6cedc61919fda6a7e4d3813f578eba4db")));
    await client.ChainSync!.FindIntersectionAsync([point], CancellationToken.None);

    Console.WriteLine("Starting chain sync...");
    while (true)
    {
        MessageNextResponse? nextResponse = await client.ChainSync.NextRequestAsync(CancellationToken.None);

        (bool shouldLog, ulong blockNo, bool isNewBlock, bool currentAtTip) = ProcessNextResponse(
            nextResponse,
            ref blocksProcessed,
            ref lastBlockNo,
            secondTimer);

        // Handle tip state changes and idle time tracking
        if (currentAtTip && !isAtTip)
        {
            // Just reached the tip
            isAtTip = true;
            if (!firstTipReached)
            {
                syncToTipTimer.Stop();
                firstTipReached = true;
                Console.WriteLine($"Reached tip after processing {blocksToFirstTip} blocks in {syncToTipTimer.Elapsed.TotalSeconds:F2} seconds");
            }
            idleTimer.Start();
            Console.WriteLine("Entering idle state at tip");
        }
        else if (!currentAtTip && isAtTip)
        {
            // No longer at tip (new blocks arrived)
            isAtTip = false;
            idleTimer.Stop();
            totalIdleTime += idleTimer.Elapsed;
            idleTimer.Reset();
            Console.WriteLine($"Exiting idle state, idle time so far: {totalIdleTime.TotalSeconds:F2}s");
        }

        // Update block counts
        if (isNewBlock)
        {
            if (!firstTipReached)
            {
                blocksToFirstTip++;
            }
            totalBlocksProcessed++;

            // Log immediately for blocks after tip
            if (isAtTip)
            {
                Console.WriteLine($"New block arrived while at tip: {blockNo}");
                shouldLog = true;
            }
        }

        // Log on timer or when explicitly requested
        if (shouldLog)
        {
            Console.WriteLine($"Processed {blocksProcessed} blocks in the last {secondTimer.ElapsedMilliseconds}ms. " +
                              $"Latest block: {lastBlockNo} | Total: {totalBlocksProcessed} blocks in " +
                              $"{totalTimer.Elapsed.TotalSeconds - totalIdleTime.TotalSeconds:F2}s active time");
            blocksProcessed = 0;
            secondTimer.Restart();
        }
    }
}

(bool shouldLog, ulong blockNo, bool isNewBlock, bool atTip) ProcessNextResponse(
    MessageNextResponse nextResponse,
    ref int blocksProcessed,
    ref ulong lastBlockNo,
    Stopwatch timer)
{
    ulong blockNo = 0;
    bool isNewBlock = false;
    bool atTip = false;

    switch (nextResponse)
    {
        case MessageRollForward messageRollForward:
            // Got a new block
            // Block? block = TestUtils.DeserializeBlockWithEra(messageRollForward.Payload.Value)!;
            // blockNo = block.Number()!.Value;
            blocksProcessed++;
            lastBlockNo = blockNo;
            isNewBlock = true;

            // Output basic block info
            //Console.WriteLine($"Block: {blockNo} | Hash: {block.Block.Hash()} | Slot: {block.Block.Slot()}");

            // We received a block, so we're definitely not at tip
            atTip = false;
            break;

        case MessageRollBackward messageRollBackward:
            blockNo = messageRollBackward.Point.Slot.Value;
            //Console.WriteLine($"ROLLBACK to slot {blockNo}");
            // After rollback, we're not at tip
            atTip = false;
            break;

        default: // MessageAwaitReply
            Console.WriteLine("Tip Reached! Waiting for next block...");
            timer.Restart();
            // This is the signal we're at tip
            atTip = true;
            // Reset blocksProcessed when reaching the tip to avoid duplicate counting
            blocksProcessed = 0;
            break;
    }

    return (timer.ElapsedMilliseconds >= 1000, blockNo, isNewBlock, atTip);
}

await Main();