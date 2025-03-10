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

Point point = new(
    new(57371845),
    new(Convert.FromHexString("20a81db38339bf6ee9b1d7e22b22c0ac4d887d332bbf4f3005db4848cd647743")));

Console.WriteLine("Finding Intersection...");
await client.ChainSync!.FindIntersectionAsync([point], CancellationToken.None);
Console.WriteLine("Intersection found");

Console.WriteLine("Initializing Db...");
await BlockDbHelper.InitializeDbAsync();

Console.WriteLine("Starting ChainSync...");

int blockCount = 0;
int deserializedCount = 0;
_ = Task.Run(async () =>
{
    while (true)
    {
        Console.WriteLine($"Block count: {blockCount}, Deserialized: {deserializedCount}, Success rate: {(deserializedCount > 0 ? deserializedCount / blockCount * 100.0 : 0.0)}%");
        blockCount = 0;
        deserializedCount = 0;
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
                Block? block = TestUtils.DeserializeBlockWithEra(msg.Payload.Value);
                blockCount++;
                deserializedCount++;
                //Console.WriteLine($"Rolling forward to block: {block!.Slot()} # {block!.Hash()}");
                await BlockDbHelper.InsertBlockAsync(block!.Number() ?? 0UL, block!.Slot() ?? 0UL, block!.Hash());
                break;
            case MessageAwaitReply msg:
                Console.WriteLine($"Block count: {blockCount}");
                blockCount = 0;
                deserializedCount = 0;
                Console.WriteLine("Tip reached!!!");
                break;
        }
    }
    catch (Exception ex)
    {
        Console.WriteLine(ex);
        throw;
    }
}
