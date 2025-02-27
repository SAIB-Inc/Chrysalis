using LanguageExt;
using static LanguageExt.Prelude;
using Chrysalis.Cbor.Serialization;
using Chrysalis.Network.Multiplexer;
using Chrysalis.Network.Cbor.Common;
using Chrysalis.Network.Cbor.ChainSync;
using Chrysalis.Cbor.Cardano.Types.Block;
using Chrysalis.Cbor.Cardano.Extensions;
using Chrysalis.Network.Cbor.LocalStateQuery;
using Chrysalis.Network.MiniProtocols.Extensions;
using System.Diagnostics;

static Aff<Unit> QueryUtox() =>
    from client in NodeClient.Connect("/tmp/intercept_node_socket")
    from result in client.LocalStateQuery
         .IfNone(() => throw new Exception("LocalStateQuery not initialized"))
         .GetUtxosByTxIn([
             new TransactionInput(new(Convert.FromHexString("30576c97934d1f88f77add233b14b0a85b65410df38c8f03b0104aaa2fdf651c")), new(0))
         ])
    from _ in Aff(() =>
    {
        Console.WriteLine($"Result: {result}");
        Console.WriteLine($"ResultCbor: {Convert.ToHexString(result.Raw!.Value.ToArray())}");
        return ValueTask.FromResult(unit);
    })
    select unit;


static Aff<Unit> ChainSync()
{
    return from nodeClient in NodeClient.Connect("/home/rawriclark/CardanoPreview/pool/txpipe/relay1/ipc/node.socket")
           from _ in nodeClient.ChainSync
               .IfNone(() => throw new Exception("ChainSync missing"))
               .FindInterection([new Point(new(73793022), new(Convert.FromHexString("1b6b4afeef73bf4a1e2a014b492549b6cedc61919fda6a7e4d3813f578eba4db")))])
           from loop in Aff(async () =>
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
                   Console.WriteLine($"Average rate to first tip: {(blocksToFirstTip / syncToTipTimer.Elapsed.TotalSeconds):F2} blocks/sec");
                   Console.WriteLine($"Total blocks processed: {totalBlocksProcessed}");
                   Console.WriteLine($"Total time: {totalTimer.Elapsed.TotalSeconds:F2} seconds");
                   Console.WriteLine($"Active sync time: {activeTime:F2} seconds");
                   Console.WriteLine($"Idle time at tip: {totalIdleTime.TotalSeconds:F2} seconds");
                   Console.WriteLine($"Overall average rate: {(totalBlocksProcessed / activeTime):F2} blocks/sec");
               };

               var chainSync = nodeClient.ChainSync
                   .IfNone(() => throw new Exception("ChainSync missing"));

               while (true)
               {
                   var nextResponseResult = await chainSync.NextRequest().Run();

                   var result = nextResponseResult.Match(
                       Succ: nextResponse => ProcessNextResponse(nextResponse, ref blocksProcessed, ref lastBlockNo, secondTimer),
                       Fail: ex => throw ex
                   );

                   var (shouldLog, blockNo, isNewBlock, currentAtTip) = result;

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
                       Console.WriteLine($"Processed {blocksProcessed} blocks in the last {secondTimer.ElapsedMilliseconds}ms. Latest block: {lastBlockNo} | Total: {totalBlocksProcessed} blocks in {(totalTimer.Elapsed.TotalSeconds - totalIdleTime.TotalSeconds):F2}s active time");
                       blocksProcessed = 0;
                       secondTimer.Restart();
                   }
               }

               return ValueTask.FromResult(unit);
           })
           from ______ in Aff(async () =>
           {
               await Task.Delay(Timeout.Infinite);
               return ValueTask.FromResult(unit);
           })
           select unit;
}

static (bool shouldLog, ulong blockNo, bool isNewBlock, bool atTip) ProcessNextResponse(
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
        case MessageRollForward response:
            // Got a new block
            blockNo = CborSerializer.Deserialize<BlockWithEra<Block>>(response.Payload.Value).Block.Number()!.Value;
            blocksProcessed++;
            lastBlockNo = blockNo;
            isNewBlock = true;
            // We received a block, so we're definitely not at tip
            atTip = false;
            break;

        case MessageRollBackward response:
            blockNo = response.Point.Slot.Value;
            Console.WriteLine($"ROLLBACK to slot {blockNo}");
            // After rollback we're not at tip
            atTip = false;
            break;

        case MessageAwaitReply _:
            Console.WriteLine("Tip Reached!");
            timer.Restart();
            // This is the signal we're at tip
            atTip = true;
            break;
    }

    return (timer.ElapsedMilliseconds >= 1000, blockNo, isNewBlock, atTip);
}

static async Task MainAsync()
{
    // Run the program to get a Fin<Unit> result.
    // var finResult = await QueryUtxo().Run();
    var finResult = await ChainSync().Run();

    // Match on the Fin result to handle success or error.
    finResult.Match(
        Succ: _ => Console.WriteLine("Program completed successfully"),
        Fail: ex => Console.WriteLine($"Program failed with error: {ex.Exception}")
    );
}

await MainAsync();