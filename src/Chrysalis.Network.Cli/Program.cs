using LanguageExt;
using static LanguageExt.Prelude;
using Chrysalis.Cbor.Serialization;
using Chrysalis.Network.Multiplexer;
using Chrysalis.Network.Cbor.LocalStateQuery;
using Chrysalis.Network.Cbor.Common;
using Chrysalis.Network.Cbor.ChainSync;
using Chrysalis.Cbor.Cardano.Types.Block;
using Chrysalis.Cbor.Cardano.Extensions;

// 007060251a30c40e428085cdb477aa0ca8462ae5d8b6a55e5e9616aeb6850282937506f53d58e9e6fd7ccbbbc57196d58b2a11e36a76a60857

static Aff<Unit> QueryUtxo() =>
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

/// <summary>
/// The entire program is expressed as a single Aff effect.
/// </summary>
static Aff<Unit> ChainSync()
{
    return from nodeClient in NodeClient.Connect("/tmp/intercept_node_socket")
           from _ in nodeClient.ChainSync
               .IfNone(() => throw new Exception("ChainSync missing"))
               .FindInterection([new Point(new(73793022), new(Convert.FromHexString("1b6b4afeef73bf4a1e2a014b492549b6cedc61919fda6a7e4d3813f578eba4db")))])
           from loop in Aff(async () =>
           {
               while (true)
               {
                   var nextResponseResult = await nodeClient.ChainSync
                   .IfNone(() => throw new Exception("ChainSync missing"))
                   .NextRequest().Run();

                   var nextResponse = nextResponseResult.Match(
                       Succ: nextResponse => NextResponseLogger(nextResponse),
                       Fail: ex => throw ex
                   );
               }
               return unit;
           })
           from ______ in Aff(async () =>
           {
               await Task.Delay(Timeout.Infinite);
               return unit;
           })
           select unit;
}

static void NextResponseLogger(MessageNextResponse nextResponse)
{
    Console.WriteLine($"Next Response: {nextResponse}");
    ulong slot = nextResponse switch
    {
        MessageRollForward response => CborSerializer.Deserialize<BlockWithEra<Block>>(response.Payload.Value).Block.Slot()!.Value,
        MessageRollBackward response => response.Point.Slot.Value,
        _ => 0,
    };
    Console.WriteLine($"Slot: {slot}");
}
static async Task MainAsync()
{
    // Run the program to get a Fin<Unit> result.
    // var finResult = await QueryUtxo().Run();
    var finResult = await ChainSync().Run();

    // Match on the Fin result to handle success or error.
    finResult.Match(
        Succ: _ => Console.WriteLine("Program completed successfully"),
        Fail: ex => Console.WriteLine($"Program failed with error: {ex.Exception}")
    );
}

await MainAsync();

//await Program().Run();