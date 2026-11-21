using System.Reflection;
using Chrysalis.Codec.Types;
using Chrysalis.Network.Cbor.Handshake;
using Chrysalis.Network.Cbor.LocalStateQuery;
using Chrysalis.Network.Cbor.LocalStateQuery.Messages;
using Chrysalis.Network.Core;
using Chrysalis.Network.MiniProtocols;
using Chrysalis.Network.MiniProtocols.Extensions;
using Chrysalis.Network.Multiplexer;
using Xunit;

namespace Chrysalis.Network.Tests;

/// <summary>Regression coverage for the narrow review's helper and custom-minimum guards.</summary>
public sealed class B1CarbonGuardTests
{
    /// <summary>Every built-in helper rejects before release/acquire, even with no incoming response.</summary>
    /// <param name="helper">The built-in helper to exercise.</param>
    /// <param name="acquired">Whether release would otherwise be the first outgoing message.</param>
    [Theory]
    [InlineData("tip", false)]
    [InlineData("era", false)]
    [InlineData("parameters", false)]
    [InlineData("address", false)]
    [InlineData("input", false)]
    [InlineData("tip", true)]
    [InlineData("era", true)]
    [InlineData("parameters", true)]
    [InlineData("address", true)]
    [InlineData("input", true)]
    public async Task BuiltInHelpersRejectInsufficientVersionBeforeAnyWrite(string helper, bool acquired)
    {
        using ProtocolReplay replay = acquired
            ? new(ProtocolType.LocalStateQuery, Convert.FromHexString("8101"))
            : new(ProtocolType.LocalStateQuery);
        await using LocalStateQuery query = new(replay.Channel, new N2CVersion(32783));
        if (acquired)
        {
            await query.AcquireAsync(null, CancellationToken.None);
            _ = replay.ReadSentPayload();
        }
        using CancellationTokenSource deadline = new(TimeSpan.FromSeconds(1));
        N2CVersionNotSupportedException failure = await Assert.ThrowsAsync<N2CVersionNotSupportedException>(() => InvokeHelper(query, helper, deadline.Token));
        Assert.Equal(32784, Assert.IsType<N2CVersion>(failure.RequiredVersion).Value);
        Assert.Equal(32783, Assert.IsType<N2CVersion>(failure.NegotiatedVersion).Value);
        Assert.Equal(nameof(LocalStateQuery.QueryAsync), failure.Operation);
        Assert.Equal(acquired, query.IsAcquired);
        replay.AssertNothingSent();
    }

    /// <summary>A present but unstarted connection context fails before release/acquire or waiting on the channel.</summary>
    /// <param name="helper">The built-in helper to exercise.</param>
    /// <param name="acquired">Whether a snapshot is already held.</param>
    [Theory]
    [InlineData("tip", false)]
    [InlineData("era", false)]
    [InlineData("parameters", false)]
    [InlineData("address", false)]
    [InlineData("input", false)]
    [InlineData("tip", true)]
    [InlineData("era", true)]
    [InlineData("parameters", true)]
    [InlineData("address", true)]
    [InlineData("input", true)]
    public async Task BuiltInHelpersRejectUnstartedContextBeforeAnyWrite(string helper, bool acquired)
    {
        using ProtocolReplay replay = acquired
            ? new(ProtocolType.LocalStateQuery, Convert.FromHexString("8101"))
            : new(ProtocolType.LocalStateQuery);
        await using LocalStateQuery query = UnstartedQuery(replay.Channel);
        if (acquired)
        {
            await query.AcquireAsync(null, CancellationToken.None);
            _ = replay.ReadSentPayload();
        }
        using CancellationTokenSource deadline = new(TimeSpan.FromSeconds(1));
        _ = await Assert.ThrowsAsync<InvalidOperationException>(() => InvokeHelper(query, helper, deadline.Token));
        Assert.Equal(acquired, query.IsAcquired);
        replay.AssertNothingSent();
    }

    /// <summary>The explicit minimum overrides the built-in Conway baseline for custom raw requests.</summary>
    [Fact]
    public async Task CustomMinimumBelowV16ReachesQuery()
    {
        using ProtocolReplay replay = new(ProtocolType.LocalStateQuery, Convert.FromHexString("8101"), PreprodFixtureTests.Wire("c1-in-p7-005.cbor"));
        await using LocalStateQuery query = new(replay.Channel, new N2CVersion(32780));
        await query.AcquireAsync(null, CancellationToken.None);
        _ = replay.ReadSentPayload();
        _ = await query.QueryAsync(new GlobalQuery(0), new N2CVersion(32780), CancellationToken.None);
        Assert.Equal(Convert.FromHexString("82038100"), replay.ReadSentPayload());
    }

    /// <summary>The explicit-minimum overload stays synchronous and fails before any outgoing message.</summary>
    [Fact]
    public async Task CustomMinimumAboveNegotiatedRejectsBeforeAnyWrite()
    {
        using ProtocolReplay replay = new(ProtocolType.LocalStateQuery);
        await using LocalStateQuery query = new(replay.Channel, new N2CVersion(32780));
        N2CVersionNotSupportedException failure = Assert.Throws<N2CVersionNotSupportedException>(() => { _ = query.QueryAsync(new GlobalQuery(0), new N2CVersion(32781), CancellationToken.None); });
        Assert.Equal(32780, Assert.IsType<N2CVersion>(failure.NegotiatedVersion).Value);
        Assert.Equal(32781, Assert.IsType<N2CVersion>(failure.RequiredVersion).Value);
        replay.AssertNothingSent();
    }

    /// <summary>The legacy overload retains its asynchronous exception boundary for argument, state and version checks.</summary>
    /// <param name="condition">The failure condition that must be returned in the task.</param>
    [Theory]
    [InlineData("null-query")]
    [InlineData("not-acquired")]
    [InlineData("low-version")]
    [InlineData("unstarted")]
    public async Task LegacyQueryOverloadDeliversGuardFailuresThroughItsTask(string condition)
    {
        using ProtocolReplay replay = new(ProtocolType.LocalStateQuery);
        await using LocalStateQuery query = condition switch
        {
            "low-version" => new(replay.Channel, new N2CVersion(32783)),
            "unstarted" => UnstartedQuery(replay.Channel),
            _ => new(replay.Channel)
        };
        Task<Result>? returned = null;
        Exception? synchronous = Record.Exception(() => { returned = query.QueryAsync(condition == "null-query" ? null! : RawQueries.GetCurrentEra, CancellationToken.None); });
        Assert.Null(synchronous);
        Assert.NotNull(returned);
        if (condition == "null-query")
        {
            _ = await Assert.ThrowsAsync<ArgumentNullException>(() => returned);
        }
        else if (condition == "low-version")
        {
            _ = await Assert.ThrowsAsync<N2CVersionNotSupportedException>(() => returned);
        }
        else
        {
            _ = await Assert.ThrowsAsync<InvalidOperationException>(() => returned);
        }
        replay.AssertNothingSent();
    }

    internal static async Task<CborRecord> InvokeHelper(LocalStateQuery query, string helper, CancellationToken token) => helper switch
    {
        "tip" => await query.GetTipAsync(token),
        "era" => await query.GetCurrentEraAsync(token),
        "parameters" => await query.GetCurrentProtocolParamsAsync(token),
        "address" => await query.GetUtxosByAddressAsync([PreprodFixtureTests.Wire("c1-out-p7-012.cbor").AsMemory(^57..)], token),
        "input" => await query.GetUtxosByTxInAsync([], token),
        _ => throw new ArgumentOutOfRangeException(nameof(helper))
    };

    private static LocalStateQuery UnstartedQuery(AgentChannel channel)
    {
        Type contextType = typeof(NodeClient).Assembly.GetType("Chrysalis.Network.Cbor.Handshake.N2CVersionContext", throwOnError: true)!;
        object context = Activator.CreateInstance(contextType, nonPublic: true)!;
        return (LocalStateQuery)Activator.CreateInstance(typeof(LocalStateQuery), BindingFlags.Instance | BindingFlags.NonPublic, null, [channel, context], null)!;
    }
}
