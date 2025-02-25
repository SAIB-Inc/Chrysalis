using LanguageExt;
using static LanguageExt.Prelude;
using Chrysalis.Network.Multiplexer;
using Chrysalis.Network.Cbor.Common;
using Chrysalis.Cbor.Serialization;
using Chrysalis.Network.Cbor.ChainSync;
using Chrysalis.Cbor.Cardano.Types.Block;
using Chrysalis.Cbor.Cardano.Extensions;

/// <summary>
/// The entire program is expressed as a single Aff effect.
/// </summary>
static Aff<Unit> Program()
{
    return from nodeClient in NodeClient.Connect("/tmp/intercept_node_socket")
           from _ in nodeClient.ChainSync
               .IfNone(() => throw new Exception("ChainSync missing"))
               .FindInterection([new Point(new(73793022), new(Convert.FromHexString("1b6b4afeef73bf4a1e2a014b492549b6cedc61919fda6a7e4d3813f578eba4db")))])
           from rollbackResponse in nodeClient.ChainSync
               .IfNone(() => throw new Exception("ChainSync missing"))
               .NextRequest()
           from __ in Aff(() =>
           {
               Console.WriteLine($"Rollback response: {rollbackResponse}");
               return ValueTask.FromResult(unit);
           })
           from rollForwardResponse1 in nodeClient.ChainSync
               .IfNone(() => throw new Exception("ChainSync missing"))
               .NextRequest()
           from ____ in Aff(() =>
           {
               NextResponseLogger(rollForwardResponse1);
               return ValueTask.FromResult(unit);
           })
           from rollForwardResponse2 in nodeClient.ChainSync
               .IfNone(() => throw new Exception("ChainSync missing"))
               .NextRequest()
           from _____ in Aff(() =>
           {
               NextResponseLogger(rollForwardResponse2);
               return ValueTask.FromResult(unit);
           })
           from loop in Aff(async () =>
           {
                while(true) {
                    var nextResponseResult = await nodeClient.ChainSync
                    .IfNone(() => throw new Exception("ChainSync missing"))
                    .NextRequest().Run();

                    var nextResponse = nextResponseResult.Match(
                        Succ: nextResponse => NextResponseLogger(nextResponse),
                        Fail: ex => Console.WriteLine($"Next request failed with error: {ex.Message}")
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
    Console.WriteLine($"Roll forward response: {nextResponse}");
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
    var finResult = await Program().Run();

    // Match on the Fin result to handle success or error.
    finResult.Match(
        Succ: _ => Console.WriteLine("Program completed successfully"),
        Fail: ex => Console.WriteLine($"Program failed with error: {ex.Message}")
    );
}

await MainAsync();