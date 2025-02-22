using Chrysalis.Network.Core;
using Chrysalis.Network.Multiplexer;

var bearer = new TcpBearer("localhost", 1234);
var plexer = new Plexer(bearer);
plexer.Spawn();

var agent = plexer.SubscribeClient(ProtocolType.Handshake);

agent.Subscribe(chunk =>
{
    Console.WriteLine($"Received chunk: {Convert.ToHexString(chunk)}");
});

await agent.EnqueueChunkAsync(Convert.FromHexString("8200a7078202f5088202f5098202f50a8202f50b8402f500f40c8402f500f40d8402f500f4"));

while (true)
{
    await Task.Delay(1000);
}