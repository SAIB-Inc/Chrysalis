using Chrysalis.Cbor.Extensions.Cardano.Core;
using Chrysalis.Cbor.Serialization;
using Chrysalis.Cbor.Types.Cardano.Core;
using Chrysalis.Cbor.Types.Cardano.Core.Header;
using Chrysalis.Network.Cbor.ChainSync;
using Chrysalis.Network.Cbor.Common;
using Chrysalis.Network.Multiplexer;

try
{
    // Connect to the Cardano node
    Console.WriteLine("Connecting to Cardano node...");
    NodeClient client = await NodeClient.ConnectAsync("/tmp/node.socket");
    await client.StartAsync();
    Console.WriteLine("Connected successfully!");

    // Test Chain-Sync protocol
    Console.WriteLine("\n=== Testing Chain-Sync Protocol ===");
    
    // Find intersection first
    Console.WriteLine("\n1. Finding intersection with known point...");
    var knownPoint = new Point(84131605, Convert.FromHexString("f93a3418fcc6017e66186f2e3c9d2baee61762c192bf5c3c582cc3b9e2424bb6"));
    Console.WriteLine($"  - Looking for intersection at slot {knownPoint.Slot}, hash {Convert.ToHexString(knownPoint.Hash)}");
    var intersectResponse = await client.ChainSync.FindIntersectionAsync([knownPoint], CancellationToken.None);
    
    switch (intersectResponse)
    {
        case MessageIntersectFound found:
            Console.WriteLine($"  - Intersection found at slot: {found.Point.Slot}");
            Console.WriteLine($"  - Tip at slot: {found.Tip.Slot.Slot}");
            break;
        case MessageIntersectNotFound notFound:
            Console.WriteLine($"  - No intersection found. Tip at slot: {notFound.Tip.Slot.Slot}");
            break;
    }

    // Test continuous sync
    Console.WriteLine("\n2. Starting continuous sync (press Ctrl+C to stop)...");
    Console.WriteLine("  - This will test the proper handling of AwaitReply at the tip");
    
    using var cts = new CancellationTokenSource();
    Console.CancelKeyPress += (s, e) => {
        e.Cancel = true;
        cts.Cancel();
    };

    int updateCount = 0;
    int awaitCount = 0;
    ulong? firstTipSlot = null;
    
    while (!cts.Token.IsCancellationRequested)
    {
        try
        {
            var response = await client.ChainSync.NextRequestAsync(cts.Token);
            
            switch (response)
            {
                case MessageRollForward rollForward:
                    updateCount++;
                    
                    // Track if we're catching up
                    firstTipSlot ??= rollForward.Tip.Slot.Slot;
                    bool isCatchingUp = rollForward.Tip.Slot.Slot == firstTipSlot;
                    
                    // Only show every 100th block when catching up
                    if (!isCatchingUp || updateCount % 100 == 1)
                    {
                        Console.WriteLine($"\n[{DateTime.Now:HH:mm:ss}] Roll forward #{updateCount}:");
                        
                        // Decode the N2C block format
                        if (rollForward.Payload?.Value != null)
                        {
                            Console.WriteLine($"  - Payload size: {rollForward.Payload.Value.Length} bytes");
                            
                            try
                            {
                                // N2C sends: Tag(24, ByteString([era, block]))
                                var reader = new System.Formats.Cbor.CborReader(rollForward.Payload.Value, System.Formats.Cbor.CborConformanceMode.Lax);
                                
                                // Read tag 24
                                var tag = reader.ReadTag();
                                if (tag != System.Formats.Cbor.CborTag.EncodedCborDataItem)
                                {
                                    throw new InvalidOperationException($"Expected CBOR tag 24, got {tag}");
                                }
                                
                                // Read the byte string containing [era, block]
                                var innerBytes = reader.ReadByteString();
                                
                                // Now deserialize BlockWithEra from the inner bytes
                                var blockWithEra = CborSerializer.Deserialize<BlockWithEra>(innerBytes);
                                
                                // Get the block
                                var block = blockWithEra.Block;
                                var era = blockWithEra.EraNumber;
                                
                                // Get block info
                                var header = block.Header();
                                var headerBody = header.HeaderBody;
                                
                                ulong? blockSlot = null;
                                ulong? blockNumber = null;
                                
                                switch (headerBody)
                                {
                                    case AlonzoHeaderBody alonzo:
                                        blockSlot = alonzo.Slot;
                                        blockNumber = alonzo.BlockNumber;
                                        break;
                                    case BabbageHeaderBody babbage:
                                        blockSlot = babbage.Slot;
                                        blockNumber = babbage.BlockNumber;
                                        break;
                                }
                                
                                if (blockSlot.HasValue)
                                {
                                    Console.WriteLine($"  - Block slot: {blockSlot}");
                                    Console.WriteLine($"  - Block number: {blockNumber}");
                                    Console.WriteLine($"  - Block hash: {header.Hash()}");
                                    Console.WriteLine($"  - Era: {era}");
                                }
                            }
                            catch (Exception ex)
                            {
                                Console.WriteLine($"  - Failed to decode block: {ex.Message}");
                                // Print hex for debugging
                                var hexString = Convert.ToHexString(rollForward.Payload.Value.AsSpan(0, Math.Min(100, rollForward.Payload.Value.Length)));
                                Console.WriteLine($"  - Payload hex (first 100 bytes): {hexString}");
                            }
                        }
                        
                        Console.WriteLine($"\n  - Current tip slot: {rollForward.Tip.Slot.Slot}");
                        Console.WriteLine($"  - Current tip hash: {Convert.ToHexString(rollForward.Tip.Slot.Hash)}");
                        
                        if (isCatchingUp)
                        {
                            Console.WriteLine($"  - Status: Catching up to tip (showing every 100th block)");
                        }
                    }
                    else if (updateCount % 1000 == 0)
                    {
                        Console.WriteLine($"  ... processed {updateCount} blocks, still catching up ...");
                    }
                    break;
                    
                case MessageRollBackward rollBack:
                    Console.WriteLine($"\n[{DateTime.Now:HH:mm:ss}] Roll backward:");
                    Console.WriteLine($"  - To slot: {rollBack.Point.Slot}");
                    Console.WriteLine($"  - Tip at: {rollBack.Tip.Slot.Slot}");
                    break;
                    
                case MessageAwaitReply:
                    awaitCount++;
                    if (awaitCount == 1)
                    {
                        Console.WriteLine($"\n[{DateTime.Now:HH:mm:ss}] Reached tip, waiting for new blocks...");
                        Console.WriteLine("  - The protocol should now handle this internally");
                        Console.WriteLine("  - Next call should wait for server update, not send a new request");
                    }
                    else if (awaitCount % 10 == 0)
                    {
                        Console.Write(".");
                    }
                    break;
            }
        }
        catch (OperationCanceledException)
        {
            break;
        }
    }

    // Send Done message to properly terminate the protocol
    Console.WriteLine("\n\n3. Sending Done message to terminate protocol...");
    await client.ChainSync.DoneAsync(CancellationToken.None);
    Console.WriteLine("Chain-Sync protocol terminated successfully!");

    // Show summary
    Console.WriteLine($"\nSummary:");
    Console.WriteLine($"  - Total updates received: {updateCount}");
    Console.WriteLine($"  - Times at tip (await): {awaitCount}");
    Console.WriteLine($"  - Protocol state: {client.ChainSync.State}");

    // Clean up
    client.Dispose();
    Console.WriteLine("\nConnection closed.");
}
catch (Exception ex)
{
    Console.WriteLine($"\nError: {ex.Message}");
    Console.WriteLine($"Stack trace: {ex.StackTrace}");
}