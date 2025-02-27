using LanguageExt;
using static LanguageExt.Prelude;
using Chrysalis.Cbor.Serialization;
using Chrysalis.Network.Multiplexer;
using Chrysalis.Network.Cbor.Common;
using Chrysalis.Network.Cbor.ChainSync;
using Chrysalis.Cbor.Cardano.Types.Block;
using Chrysalis.Cbor.Cardano.Extensions;
using Chrysalis.Network.Cbor.LocalStateQuery;
using Chrysalis.Network.MiniProtocols.Extensions;

static Aff<Unit> QueryUtox() =>
    from client in NodeClient.Connect("/tmp/intercept_node_socket")
    from result in client.LocalStateQuery
         .IfNone(() => throw new Exception("LocalStateQuery not initialized"))
         .GetUtxosByTxIn([
             new TransactionInput(new(Convert.FromHexString("30576c97934d1f88f77add233b14b0a85b65410df38c8f03b0104aaa2fdf651c")), new(0))
         ])
    from _ in Aff(() =>
    {
        Console.WriteLine($"Result: {result}");
        Console.WriteLine($"ResultCbor: {Convert.ToHexString(result.Raw!.Value.ToArray())}");
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
        MessageAwaitReply response => 0,
        _ => 0,
    };
    if (slot > 0)
        Console.WriteLine($"Slot: {slot}");
    else
        Console.WriteLine("Tip Reached!");
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