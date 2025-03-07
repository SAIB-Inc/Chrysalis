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
                Block? block = TestUtils.DeserializeBlockWithEra(msg.Payload.Value);
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