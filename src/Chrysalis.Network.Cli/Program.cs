using LanguageExt;
using static LanguageExt.Prelude;
using Chrysalis.Network.Multiplexer;
using Chrysalis.Network.Cbor.Common;

/// <summary>
/// The entire program is expressed as a single Aff effect.
/// </summary>
static Aff<Unit> Program() => 
    from nodeClient in NodeClient.Connect("/tmp/intercept_node_socket")
    from _ in nodeClient.ChainSync
        .IfNone(() => throw new Exception("ChainSync missing"))
        .FindInterection([new Point(new(73793022), new(Convert.FromHexString("1b6b4afeef73bf4a1e2a014b492549b6cedc61919fda6a7e4d3813f578eba4db")))])
    from __ in Aff(async () => {
        await Task.Delay(Timeout.Infinite);
        return unit;
    })
    select unit;

await Program().Run();