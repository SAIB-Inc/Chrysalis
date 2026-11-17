using Chrysalis.Network.Core;
using Chrysalis.Network.Multiplexer;
using Xunit;

namespace Chrysalis.Network.Tests.Receive;

public sealed class ReceiveRiskTests
{
    [Fact]
    public async Task S9EofInsideSegmentEndsInsteadOfSpinning()
    {
        using ScriptedReceiveBearer bearer = new();
        using Demuxer demuxer = new(bearer);
        using CancellationTokenSource cleanup = new();
        _ = demuxer.Subscribe(ProtocolType.BlockFetch);
        bearer.Deliver([0, 0, 0], end: true);
        Task run = Task.Run(() => demuxer.RunAsync(cleanup.Token));
        try { _ = await Assert.ThrowsAsync<EndOfStreamException>(() => run.WaitAsync(TimeSpan.FromSeconds(2))); }
        finally { await cleanup.CancelAsync(); _ = await Record.ExceptionAsync(() => run); }
    }
}
