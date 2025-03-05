using Chrysalis.Network.Cbor.Common;
using Chrysalis.Network.Cbor.Handshake;
using Chrysalis.Network.Multiplexer;

NodeClient client = await NodeClient.ConnectAsync("/tmp/intercept_node_socket");
client.Start();
var handshakeResult = await client.Handshake!.SendAsync(HandshakeMessages.ProposeVersions(VersionTables.N2C_V10_AND_ABOVE), CancellationToken.None);
var point = new Point(new(60722104), new(Convert.FromHexString("183449e2b91a508e807153715ae7ff82ea04efbc053653ad9ebb70f1bd3f9eeb")));
await client.ChainSync.FindIntersectionAsync([point], CancellationToken.None);

int blockCount = 0;
while (true)
{
    var nextResponse = await client.ChainSync.NextRequestAsync(CancellationToken.None);
    Console.WriteLine(nextResponse);
    Console.WriteLine("BlockCount: {0}", ++blockCount);
}