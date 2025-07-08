using Chrysalis.Network.Cbor.ChainSync;
using Chrysalis.Network.Cbor.Common;
using Chrysalis.Network.MiniProtocols.Extensions;
using Chrysalis.Network.Multiplexer;

try
{
    // Connect to the Cardano node
    Console.WriteLine("Connecting to Cardano node...");
    NodeClient client = await NodeClient.ConnectAsync("/tmp/node.socket");
    await client.StartAsync();
    Console.WriteLine("Connected successfully!");

    // Query initial tip
    Console.WriteLine("\nQuerying initial tip...");
    var initialTip = await client.LocalStateQuery.GetTipAsync();
    Console.WriteLine($"Initial tip: Slot {initialTip.Slot.Slot}, Hash {Convert.ToHexString(initialTip.Slot.Hash)}");

    // Start ChainSync from the initial tip
    Console.WriteLine("\nStarting ChainSync from initial tip...");
    var intersectionPoint = new Point(initialTip.Slot.Slot, initialTip.Slot.Hash);
    var intersectResponse = await client.ChainSync.FindIntersectionAsync([intersectionPoint], CancellationToken.None);
    
    if (intersectResponse is MessageIntersectFound found)
    {
        Console.WriteLine($"Intersection found at slot {found.Point.Slot}");
    }

    // Create cancellation token for graceful shutdown
    using var cts = new CancellationTokenSource();
    Console.CancelKeyPress += (s, e) => {
        e.Cancel = true;
        cts.Cancel();
    };

    // Start ChainSync in background task
    var chainSyncTask = Task.Run(async () =>
    {
        int blockCount = 0;
        bool atTip = false;
        Console.WriteLine("\n[ChainSync] Starting to sync blocks...");
        
        while (!cts.Token.IsCancellationRequested)
        {
            try
            {
                var response = await client.ChainSync.NextRequestAsync(cts.Token);
                
                switch (response)
                {
                    case MessageRollForward rollForward:
                        blockCount++;
                        
                        // Always log when at tip (receiving new blocks)
                        if (atTip)
                        {
                            Console.WriteLine($"[ChainSync] New block! Slot: {rollForward.Tip.Slot.Slot}, Total blocks: {blockCount}");
                        }
                        // When catching up, only log every 100th block
                        else if (blockCount % 100 == 1)
                        {
                            Console.WriteLine($"[ChainSync] Catching up... Processed {blockCount} blocks, current tip: {rollForward.Tip.Slot.Slot}");
                        }
                        break;
                        
                    case MessageAwaitReply:
                        if (!atTip)
                        {
                            Console.WriteLine($"[ChainSync] Reached tip! Processed {blockCount} blocks. Waiting for new blocks...");
                            atTip = true;
                        }
                        break;
                        
                    case MessageRollBackward rollBack:
                        Console.WriteLine($"[ChainSync] Roll backward to slot: {rollBack.Point.Slot}");
                        break;
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
        
        Console.WriteLine($"[ChainSync] Stopped. Total blocks processed: {blockCount}");
    });

    // Start tip query loop in background
    var tipQueryTask = Task.Run(async () =>
    {
        Console.WriteLine("\n[TipQuery] Starting to query tips every 3 seconds...");
        
        ulong? lastSlot = null;
        while (!cts.Token.IsCancellationRequested)
        {
            try
            {
                var currentTip = await client.LocalStateQuery.GetTipAsync(cts.Token);
                
                // Only log if the tip changed or it's the first query
                if (lastSlot == null || lastSlot != currentTip.Slot.Slot)
                {
                    Console.WriteLine($"[TipQuery] Tip updated! Slot: {currentTip.Slot.Slot} (was: {lastSlot?.ToString() ?? "N/A"})");
                    lastSlot = currentTip.Slot.Slot;
                }
                
                await Task.Delay(3000, cts.Token); // Query every 3 seconds
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
        
        Console.WriteLine("[TipQuery] Stopped.");
    });

    // Wait for user to stop
    Console.WriteLine("\nPress Ctrl+C to stop...\n");
    
    // Wait for both tasks to complete
    await Task.WhenAll(chainSyncTask, tipQueryTask);

    // Clean up
    await client.ChainSync.DoneAsync(CancellationToken.None);
    client.Dispose();
    Console.WriteLine("\nConnection closed.");
}
catch (Exception ex)
{
    Console.WriteLine($"\nError: {ex.Message}");
    Console.WriteLine($"Stack trace: {ex.StackTrace}");
}