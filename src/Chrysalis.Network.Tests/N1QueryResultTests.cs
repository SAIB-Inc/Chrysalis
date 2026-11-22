using System.Security.Cryptography;
using Chrysalis.Codec.Serialization;
using Chrysalis.Codec.Types;
using Chrysalis.Network.Cbor.Common;
using Chrysalis.Network.Core;
using Chrysalis.Network.MiniProtocols;
using Xunit;

namespace Chrysalis.Network.Tests;

// D17 provenance (Apache-2.0): ouroboros-consensus release-ouroboros-consensus-5.4.0.0 (808e4005),
// ouroboros-consensus-cardano/golden/cardano/QueryVersion3/CardanoNodeToClientVersion19/
// Result_EraMismatchByron and Result_EraMismatchShelley; MsgResult framing from ouroboros-network Codec.hs:112-115.
public sealed class N1QueryResultTests
{
    internal const string Byron = "828201675368656c6c65798200654279726f6e";
    internal const string Shelley = "828200654279726f6e8201675368656c6c6579";

    public static IEnumerable<object[]> Mismatches()
    {
        string[] helpers = ["tip", "parameters", "address", "input"];
        (string Hex, string Sha256, string QueryEra, string LedgerEra)[] cases =
        [
            (Byron, "db0b7e1431611d06652fa74798465303e2128ffd6090ee78f9fbeb9a7695ff06", "Shelley (1)", "Byron (0)"),
            (Shelley, "bdcc7be632b8438ad6d58f4e3b0cd629da8244b2be6aa896115ee2c007be2c31", "Byron (0)", "Shelley (1)")
        ];
        return from helper in helpers from mismatch in cases select new object[] { helper, mismatch.Hex, mismatch.Sha256, mismatch.QueryEra, mismatch.LedgerEra };
    }

    [Theory]
    [InlineData("tip", 3)]
    [InlineData("era", 5)]
    [InlineData("parameters", 7)]
    [InlineData("address", 9)]
    [InlineData("input", 11)]
    public async Task HelperReturnsTheCapturedResultAndReleases(string helper, int sequence)
    {
        byte[] reply = PreprodFixtureTests.Wire($"c1-in-p7-{sequence:D3}.cbor");
        using ProtocolReplay replay = new(ProtocolType.LocalStateQuery, PreprodFixtureTests.Wire($"c1-in-p7-{sequence - 1:D3}.cbor"), reply);
        await using LocalStateQuery query = new(replay.Channel);
        using CancellationTokenSource deadline = new(TimeSpan.FromSeconds(10));
        CborRecord answer = await B1CarbonGuardTests.InvokeHelper(query, helper, deadline.Token);
        Assert.Equal(reply[2..], answer.Raw.ToArray());
        Assert.Null((answer as Tip)?.BlockNumber);
        Assert.False(query.IsAcquired);
    }

    [Theory]
    [MemberData(nameof(Mismatches))]
    public async Task EraMismatchNamesTheQueryAndLedgerEras(string helper, string hex, string sha256, string queryEra, string ledgerEra)
    {
        byte[] golden = Convert.FromHexString(hex);
        Assert.Equal(sha256, Convert.ToHexStringLower(SHA256.HashData(golden)));
        using ProtocolReplay replay = new(ProtocolType.LocalStateQuery, PreprodFixtureTests.Wire("c1-in-p7-002.cbor"), [0x82, 0x04, .. golden]);
        await using LocalStateQuery query = new(replay.Channel);
        using CancellationTokenSource deadline = new(TimeSpan.FromSeconds(10));
        InvalidOperationException failure = await Assert.ThrowsAsync<InvalidOperationException>(() => B1CarbonGuardTests.InvokeHelper(query, helper, deadline.Token));
        Assert.Equal($"Era mismatch: the query is for {queryEra}, but the node's ledger is in {ledgerEra}.", failure.Message);
        Assert.False(query.IsAcquired);
    }

    // SYNTHETIC typed-error replies (exempt from D17): wrong payload type, era with null name,
    // era with three items, and the era-mismatch golden sent to the non-era-specific era query.
    [Theory]
    [InlineData("input", "82048101")]
    [InlineData("input", "8204828201f68200654279726f6e")]
    [InlineData("input", "8204828301675368656c6c65798200654279726f6e8200654279726f6e")]
    [InlineData("era", "8204" + Byron)]
    public async Task UndecodableResultIsAnInvalidOperation(string helper, string reply)
    {
        using ProtocolReplay replay = new(ProtocolType.LocalStateQuery, PreprodFixtureTests.Wire("c1-in-p7-002.cbor"), Convert.FromHexString(reply));
        await using LocalStateQuery query = new(replay.Channel);
        using CancellationTokenSource deadline = new(TimeSpan.FromSeconds(10));
        InvalidOperationException failure = await Assert.ThrowsAsync<InvalidOperationException>(() => B1CarbonGuardTests.InvokeHelper(query, helper, deadline.Token));
        Assert.StartsWith("Failed to deserialize response: ", failure.Message, StringComparison.Ordinal);
        _ = Assert.IsAssignableFrom<CborDecodeException>(failure.InnerException);
        Assert.False(query.IsAcquired);
    }
}
