using System.Text.Json;
using Chrysalis.Cbor.Serialization;
using Chrysalis.Network.Cbor;
using Chrysalis.Network.Core;
using Chrysalis.Network.Multiplexer;

var bearer = new TcpBearer("localhost", 1234);
var plexer = new Plexer(bearer);
plexer.Spawn();

var agent = plexer.SubscribeClient(ProtocolType.Handshake);

agent.Subscribe(chunk =>
{
    try
    {
        var handshakeMessage = CborSerializer.Deserialize<HandshakeMessage>(chunk);
        Console.WriteLine($"Received chunk: {JsonSerializer.Serialize(handshakeMessage)}");
    }
    catch (Exception ex)
    {
        Console.WriteLine($"Error deserializing chunk: {ex.Message}");
    }
});

var proposeMessage = CborSerializer.Deserialize<HandshakeMessage>(Convert.FromHexString("8200a7078202f5088202f5098202f50a8202f50b8402f500f40c8402f500f40d8402f500f4"));
Console.WriteLine("Sending propose message... {0}", JsonSerializer.Serialize(proposeMessage));
await agent.EnqueueChunkAsync(CborSerializer.Serialize(proposeMessage));

while (true)
{
    await Task.Delay(1000);
}
