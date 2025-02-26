using System;
using System.Threading.Tasks;
using LanguageExt;
using static LanguageExt.Prelude;
using Chrysalis.Cbor.Serialization;
using Chrysalis.Network.Core;
using Chrysalis.Network.Multiplexer;
using Chrysalis.Network.Cbor;
using Chrysalis.Network.MiniProtocols;
using LanguageExt.Common;
using Chrysalis.Cbor.Cardano.Types.Block.Transaction.Output;
using Chrysalis.Network.Cbor.LocalStateQuery;

// 007060251a30c40e428085cdb477aa0ca8462ae5d8b6a55e5e9616aeb6850282937506f53d58e9e6fd7ccbbbc57196d58b2a11e36a76a60857

static Aff<Unit> Program() =>
    from client in NodeClient.Connect("/tmp/intercept_node_socket")
    from tipResult in client.LocalStateQuery
         .IfNone(() => throw new Exception("LocalStateQuery not initialized"))
         .Query(
            point: null,
            // query : Queries.GetTip
            query: Queries.GetUtxoByAddress(
                [Convert.FromHexString("007060251a30c40e428085cdb477aa0ca8462ae5d8b6a55e5e9616aeb6850282937506f53d58e9e6fd7ccbbbc57196d58b2a11e36a76a60857")]
            )
         )
    from _ in Aff(() =>
    {
        Console.WriteLine($"Tip: {tipResult}");
        return ValueTask.FromResult(unit);
    })
    select unit;

async Task MainAsync()
{
    // Run the program to get a Fin<Unit> result.
    var finResult = await Program().Run();

    // Match on the Fin result to handle success or error.
    finResult.Match(
        Succ: _ => Console.WriteLine("Program completed successfully"),
        Fail: ex => Console.WriteLine($"Program failed with error: {ex.Message}")
    );
}

await MainAsync();

//await Program().Run();