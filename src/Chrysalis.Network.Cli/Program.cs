using System.Text.Json;
using Chrysalis.Network.Cbor.ChainSync;
using Chrysalis.Network.Cbor.Common;
using Chrysalis.Network.Cli;
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
    
    // Request the first update
    Console.WriteLine("\n1. Requesting first update...");
    var response1 = await client.ChainSync.NextRequestAsync(CancellationToken.None);
    Console.WriteLine($"Response type: {response1?.GetType().Name}");
    
    switch (response1)
    {
        case MessageRollForward rollForward:
            Console.WriteLine($"  - Rolled forward to tip slot: {rollForward.Tip.Slot.Slot}");
            Console.WriteLine($"  - Tip hash: {Convert.ToHexString(rollForward.Tip.Slot.Hash)}");
            Console.WriteLine($"  - Block number: {rollForward.Tip.BlockNumber}");
            break;
        case MessageRollBackward rollBack:
            Console.WriteLine($"  - Rolled backward to slot: {rollBack.Point.Slot}");
            break;
        case MessageAwaitReply:
            Console.WriteLine("  - Node says to wait (we're at the tip)");
            break;
    }

    // Request another update
    Console.WriteLine("\n2. Requesting second update...");
    var response2 = await client.ChainSync.NextRequestAsync(CancellationToken.None);
    Console.WriteLine($"Response type: {response2?.GetType().Name}");
    
    // Find intersection with a known point (optional)
    Console.WriteLine("\n3. Testing intersection finding...");
    var knownPoint = new Point(84131605, Convert.FromHexString("f93a3418fcc6017e66186f2e3c9d2baee61762c192bf5c3c582cc3b9e2424bb6"));
    Console.WriteLine($"  - Looking for intersection at slot {knownPoint.Slot}, hash {Convert.ToHexString(knownPoint.Hash)}");
    var intersectResponse = await client.ChainSync.FindIntersectionAsync([knownPoint], CancellationToken.None);
    
    switch (intersectResponse)
    {
        case MessageIntersectFound found:
            Console.WriteLine($"  - Intersection found at slot: {found.Point.Slot}");
            break;
        case MessageIntersectNotFound notFound:
            Console.WriteLine($"  - No intersection found. Tip at slot: {notFound.Tip.Slot.Slot}");
            break;
    }

    // Send Done message to properly terminate the protocol
    Console.WriteLine("\n4. Sending Done message to terminate protocol...");
    await client.ChainSync.DoneAsync(CancellationToken.None);
    Console.WriteLine("Chain-Sync protocol terminated successfully!");

    // Clean up
    client.Dispose();
    Console.WriteLine("\nConnection closed.");
}
catch (Exception ex)
{
    Console.WriteLine($"\nError: {ex.Message}");
    Console.WriteLine($"Stack trace: {ex.StackTrace}");
}