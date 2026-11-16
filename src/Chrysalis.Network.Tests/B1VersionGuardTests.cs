using System.Reflection;
using Chrysalis.Network.Cbor.Handshake;
using Chrysalis.Network.Cbor.LocalStateQuery;
using Chrysalis.Network.Cbor.LocalTxMonitor;
using Chrysalis.Network.Core;
using Chrysalis.Network.MiniProtocols;
using Chrysalis.Network.Multiplexer;
using Xunit;

namespace Chrysalis.Network.Tests;

/// <summary>Negotiated-version no-write guards and the approved standalone-handler compatibility policy.</summary>
public sealed class B1VersionGuardTests
{
    /// <summary>GetMeasures is a V20 operation and must never be written on a lower negotiated version.</summary>
    /// <param name="wireVersion">The negotiated wire version.</param>
    [Theory]
    [InlineData(32784)]
    [InlineData(32785)]
    [InlineData(32786)]
    [InlineData(32787)]
    public async Task MonitorRejectsUnsupportedMeasuresBeforeWriting(int wireVersion)
    {
        using ProtocolReplay replay = new(ProtocolType.LocalTxMonitor);
        LocalTxMonitor monitor = ExplicitMonitor(replay.Channel, wireVersion);
        Exception failure = await Assert.ThrowsAnyAsync<Exception>(() => monitor.GetMeasuresAsync(CancellationToken.None));
        AssertVersionFailure(failure, wireVersion, 32788);
        replay.AssertNothingSent();
    }

    /// <summary>A supported explicit context still permits the existing monitor operation.</summary>
    [Fact]
    public async Task MonitorAllowsSupportedMeasures()
    {
        // SYNTHETIC reply [12,0,{}], used only to test guard/send behavior, not node measure semantics.
        using ProtocolReplay replay = new(ProtocolType.LocalTxMonitor, Convert.FromHexString("830c00a0"));
        LocalTxMonitor monitor = ExplicitMonitor(replay.Channel, 32788);
        _ = Assert.IsType<ReplyGetMeasures>(await monitor.GetMeasuresAsync(CancellationToken.None));
        Assert.Equal(Convert.FromHexString("810b"), await replay.ReadSentPayloadAsync(CancellationToken.None));
    }

    /// <summary>The old standalone monitor constructor does not invent a negotiated version or fail closed.</summary>
    [Fact]
    public async Task StandaloneMonitorWithoutContextBehavesAsBefore()
    {
        using ProtocolReplay replay = new(ProtocolType.LocalTxMonitor, Convert.FromHexString("830c00a0"));
        LocalTxMonitor monitor = new(replay.Channel);
        _ = Assert.IsType<ReplyGetMeasures>(await monitor.GetMeasuresAsync(CancellationToken.None));
        Assert.Equal(Convert.FromHexString("810b"), await replay.ReadSentPayloadAsync(CancellationToken.None));
    }

    /// <summary>The current built-in Conway query family needs at least V16 when a real context is present.</summary>
    [Fact]
    public async Task StateQueryRejectsBelowItsBaselineBeforeWriting()
    {
        using ProtocolReplay replay = new(ProtocolType.LocalStateQuery);
        await using LocalStateQuery query = ExplicitQuery(replay.Channel, 32783);
        Exception failure = await Assert.ThrowsAnyAsync<Exception>(() => query.QueryAsync(RawQueries.GetCurrentEra, CancellationToken.None));
        AssertVersionFailure(failure, 32783, 32784);
        replay.AssertNothingSent();
    }

    /// <summary>The old standalone LSQ constructor retains its built-in-query behavior without invented context.</summary>
    [Fact]
    public async Task StandaloneStateQueryWithoutContextBehavesAsBefore()
    {
        // SYNTHETIC reply container accepted by the legacy Result type. This test checks only
        // guard compatibility; real node Result wire expectations remain the separate Known B2 goldens.
        using ProtocolReplay replay = new(ProtocolType.LocalStateQuery, Convert.FromHexString("8101"), Convert.FromHexString("8204d8184106"));
        await using LocalStateQuery query = new(replay.Channel);
        await query.AcquireAsync(null, CancellationToken.None);
        _ = await replay.ReadSentPayloadAsync(CancellationToken.None);
        _ = await query.QueryAsync(RawQueries.GetCurrentEra, CancellationToken.None);
        Assert.Equal(PreprodFixtures.Read("wire/c4-out-p7-003.cbor"), await replay.ReadSentPayloadAsync(CancellationToken.None));
    }

    private static LocalTxMonitor ExplicitMonitor(AgentChannel channel, int version)
    {
        ConstructorInfo? constructor = typeof(LocalTxMonitor).GetConstructor([typeof(AgentChannel), typeof(N2CVersion)]);
        Assert.NotNull(constructor);
        return Assert.IsType<LocalTxMonitor>(constructor.Invoke([channel, new N2CVersion(version)]));
    }

    private static LocalStateQuery ExplicitQuery(AgentChannel channel, int version)
    {
        ConstructorInfo? constructor = typeof(LocalStateQuery).GetConstructor([typeof(AgentChannel), typeof(N2CVersion)]);
        Assert.NotNull(constructor);
        return Assert.IsType<LocalStateQuery>(constructor.Invoke([channel, new N2CVersion(version)]));
    }

    private static void AssertVersionFailure(Exception failure, int negotiated, int required)
    {
        Assert.Equal("N2CVersionNotSupportedException", failure.GetType().Name);
        PropertyInfo? requiredProperty = failure.GetType().GetProperty("RequiredVersion");
        PropertyInfo? negotiatedProperty = failure.GetType().GetProperty("NegotiatedVersion");
        Assert.NotNull(requiredProperty);
        Assert.NotNull(negotiatedProperty);
        Assert.Equal(required, Assert.IsType<N2CVersion>(requiredProperty.GetValue(failure)).Value);
        Assert.Equal(negotiated, Assert.IsType<N2CVersion>(negotiatedProperty.GetValue(failure)).Value);
    }
}
