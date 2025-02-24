using System;
using System.Threading.Tasks;
using LanguageExt;
using static LanguageExt.Prelude;
using Chrysalis.Cbor.Serialization;
using Chrysalis.Network.Core;
using Chrysalis.Network.Multiplexer;
using Chrysalis.Network.Cbor;

/// <summary>
/// The entire program is expressed as a single Aff effect.
/// </summary>
static Aff<Unit> Program() =>
    // Create the bearer as an effect.
    from bearer in TcpBearer.CreateAsync("localhost", 1234)
        // Acquire a Plexer resource using our custom bracket helper.
    from _ in Bracket(
         // Resource acquisition: create a new Plexer from the bearer.
         () => new Plexer(bearer),
         // Resource use: run the plexer run loop, handle messages, and then await an infinite delay.
         plexer =>
             from __ in plexer.Run()           // spawn the run loop (fire‑and‑forget internally)
             from ___ in HandleMessages(plexer)
             from ____ in DelayInfinite()      // keep the program alive
             select unit,
         // Resource release: dispose the plexer.
         plexer => plexer.Dispose()
    )
    select unit;

/// <summary>
/// Composes the subscription and sending of a propose message as an Aff effect.
/// </summary>
static Aff<Unit> HandleMessages(Plexer plexer)
{
    // Subscribe to the Handshake channel.
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

    // Return the effect that sends the propose message.
    return agent.EnqueueChunk(CborSerializer.Serialize(proposeMessage));
}

/// <summary>
/// A helper that returns an Aff effect which delays forever.
/// </summary>
static Aff<Unit> DelayInfinite() =>
    Aff(async () =>
    {
        await Task.Delay(Timeout.Infinite);
        return unit;
    });

/// <summary>
/// A custom bracket helper that acquires a resource, uses it, and then releases it.
/// </summary>
/// <typeparam name="A">The resource type.</typeparam>
/// <typeparam name="R">The result type.</typeparam>
/// <param name="acquire">A function to acquire the resource.</param>
/// <param name="use">A function that uses the resource, returning an Aff effect.</param>
/// <param name="release">An action to release the resource.</param>
/// <returns>An Aff effect yielding the result R.</returns>
static Aff<R> Bracket<A, R>(Func<A> acquire, Func<A, Aff<R>> use, Action<A> release) =>
    Aff(async () =>
    {
        A resource = acquire();
        try
        {
            // use(resource).Run() returns a Fin<R>, so unwrap it:
            var finResult = await use(resource).Run();
            return finResult.Match(r => r, ex => { throw ex; });
        }
        finally
        {
            release(resource);
        }
    });

await Program().Run();