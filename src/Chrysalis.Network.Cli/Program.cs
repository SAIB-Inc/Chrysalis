using System;
using System.Threading;
using System.Threading.Tasks;
using Chrysalis.Cbor.Serialization;
using Chrysalis.Network.Cbor;
using Chrysalis.Network.Core;
using Chrysalis.Network.Multiplexer;
using LanguageExt;
using static LanguageExt.Prelude;

async Task MainAsync()
{
    // Create the bearer as an Aff effect, run it, and unwrap the result using Match.
    var bearerFin = await TcpBearer.CreateAsync("localhost", 1234).Run();
    IBearer bearer = bearerFin.Match(
        b => b,
        ex => { throw ex; }
    );

    // Create a plexer using the functional style.
    using var plexer = new Plexer(bearer);

    // Create a CancellationTokenSource for the plexer run loop.
    var cts = new CancellationTokenSource();

    // Start the plexer run loop concurrently as an Aff effect.
    var plexerRun = plexer.Run();

    // Fire-and-forget the plexer run loop.
    _ = plexerRun.Run();

    // Subscribe to the agent channel for Handshake messages.
    var agent = plexer.SubscribeClient(ProtocolType.Handshake);
    agent.Subscribe(chunk =>
    {
        // Use Try to capture any deserialization errors.
        Try(() => CborSerializer.Deserialize<HandshakeMessage>(chunk))
            .Match(
                handshakeMessage => Console.WriteLine($"Received chunk: {handshakeMessage}"),
                ex => Console.WriteLine($"Error deserializing chunk: {ex.Message}")
            );
    });

    // Create a propose message from a hex string.
    var proposeMessage = CborSerializer.Deserialize<HandshakeMessage>(
        Convert.FromHexString("8200a7078202f5088202f5098202f50a8202f50b8402f500f40c8402f500f40d8402f500f4")
    );
    Console.WriteLine("Sending propose message... {0}\n", proposeMessage);

    // Send the propose message using the functional EnqueueChunk effect.
    await agent.EnqueueChunk(CborSerializer.Serialize(proposeMessage)).Run();

    // Keep the program running indefinitely.
    await Task.Delay(Timeout.Infinite);
}

await MainAsync();
