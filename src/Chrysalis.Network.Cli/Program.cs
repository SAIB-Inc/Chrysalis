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
static Aff<Unit> Program() => from _ in Aff(() =>
    {
        Console.WriteLine("Starting the program...");
        return NodeClient.Connect("/tmp/intercept_node_socket").Run();
    })
    select unit;

await Program().Run();