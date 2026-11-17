using Xunit;
using Env = Chrysalis.IntegrationTests.IntegrationEnvironment;

namespace Chrysalis.IntegrationTests;

/// <summary>Explicit DijkstraNet probes; never selected by an offline suite.</summary>
public sealed class DijkstraIntegrationTests
{
    internal const string Enabled = "CHRYSALIS_DIJKSTRA_PROBES";

    /// <summary>Keeps every block and header in a contiguous window across the real fork.</summary>
    [IntegrationFact(Enabled, Env.Host, Env.Port, Env.Magic, Env.Output)]
    [Trait("Category", "Integration")]
    public async Task NodeToNodeSyncKeepsEveryBlockAcrossTheFork()
    {
        Assert.Equal("1", Env.Required(Enabled));
        Assert.Equal(6UL, Env.NetworkMagic);
        await DijkstraProbes.CaptureForkAsync();
    }

    /// <summary>Submits a Chrysalis-built Conway transaction and keeps the Dijkstra node's answer.</summary>
    [IntegrationFact(Enabled, Env.Socket, Env.Magic, Env.Cli, Env.NodeVersion, Env.Output, Env.WalletFile, Env.WalletAddress, Env.FundedOptIn)]
    [Trait("Category", "Integration")]
    public async Task ConwaySelfTransferKeepsTheDijkstraNodesAnswer()
    {
        Assert.Equal("1", Env.Required(Enabled));
        Assert.Equal(6UL, Env.NetworkMagic);
        Env.RequireFundedOptIn();
        await DijkstraProbes.SubmitConwayAsync();
    }
}
