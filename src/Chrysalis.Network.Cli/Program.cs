using Chrysalis.Network.Cbor.Handshake;
using Chrysalis.Network.Multiplexer;

NodeClient client = await NodeClient.ConnectAsync("/tmp/intercept_node_socket");
client.Start();
await client.Handshake!.SendAsync(HandshakeMessages.ProposeVersions(VersionTables.N2C_V10_AND_ABOVE), CancellationToken.None);