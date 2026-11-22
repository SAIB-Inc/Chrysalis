using System.Security.Cryptography;
using Chrysalis.Codec.Serialization;
using Chrysalis.Codec.Types;
using Chrysalis.Codec.Types.Cardano.Core;
using Chrysalis.Codec.Types.Cardano.Core.Protocol;
using Chrysalis.Network.Cbor.Common;
using Chrysalis.Network.Cbor.LocalStateQuery;
using Chrysalis.Network.Core;
using Chrysalis.Network.MiniProtocols;
using Xunit;

namespace Chrysalis.Network.Tests;

// D17 provenance (Apache-2.0): ouroboros-consensus release-ouroboros-consensus-5.4.0.0 (808e4005),
// ouroboros-consensus-cardano/golden/cardano/QueryVersion3/CardanoNodeToClientVersion19/
// Result_EraMismatchByron, Result_EraMismatchShelley, the Babbage and Conway GetLedgerTip and GetCurrentPParams queries, and
// Result_Babbage_LedgerTip and Result_Babbage_EmptyPParams;
// MsgResult framing from ouroboros-network Codec.hs:112-115.
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

    // Eras 1 and 5 send that era's request but replay the Conway capture as the result: those rows check the request side only.
    [Theory]
    [InlineData("tip", 3, 6, "c1-out-p7-003")]
    [InlineData("tip", 3, 5, "c1-out-p7-003")]
    [InlineData("tip", 3, 1, "c1-out-p7-003")]
    [InlineData("era", 5, 6, null)]
    [InlineData("parameters", 7, 6, "c1-out-p7-009")]
    [InlineData("parameters", 7, 5, "c1-out-p7-009")]
    [InlineData("address", 9, 6, "c1-out-p7-012")]
    [InlineData("address", 9, 5, "c1-out-p7-012")]
    [InlineData("input", 11, 6, "c1-out-p7-015")]
    [InlineData("input", 11, 5, "c1-out-p7-015")]
    public async Task HelperQueriesInTheLedgerEraAndReturnsTheCapturedResult(string helper, int sequence, int era, string? conwayQuery)
    {
        byte[] reply = PreprodFixtureTests.Wire($"c1-in-p7-{sequence:D3}.cbor");
        using ProtocolReplay replay = Conversation(helper, era, reply);
        await using LocalStateQuery query = new(replay.Channel);
        using CancellationTokenSource deadline = new(TimeSpan.FromSeconds(10));
        CborRecord answer = await B1CarbonGuardTests.InvokeHelper(query, helper, deadline.Token);
        Assert.Equal(reply[2..], answer.Raw.ToArray());
        Assert.Null((answer as Tip)?.BlockNumber);
        Assert.False(query.IsAcquired);
        // Acquire, the era query as cardano-cli sends it (c4-out-p7-003), the query in the answered era, release.
        List<byte[]> sent = replay.ReadSentPayloads();
        Assert.Equal(conwayQuery is null ? 3 : 4, sent.Count);
        Assert.Equal(PreprodFixtureTests.Wire("c1-out-p7-002.cbor"), sent[0]);
        Assert.Equal(PreprodFixtureTests.Wire("c4-out-p7-003.cbor"), sent[1]);
        if (conwayQuery is not null)
        {
            // G1's Conway query, which the node answered, with the era byte of [3, [0, [0, [era, query]]]] set.
            byte[] expected = PreprodFixtureTests.Wire($"{conwayQuery}.cbor");
            expected[7] = (byte)era;
            Assert.Equal(expected, sent[2]);
        }
        Assert.Equal(PreprodFixtureTests.Wire("c1-out-p7-004.cbor"), sent[^1]);
    }

    // D17: the block queries [0, [era, query]] of Query_<era>_GetLedgerTip and Query_<era>_GetCurrentPParams,
    // inside the query envelope [0, block query] (ouroboros-consensus 5.4.0.0 Ledger/Query.hs:420-427).
    [Theory]
    [InlineData("tip", 5, "820082058100", "974dfab2a8018adfe9b5eef72f38af9f29148c81cc1cd85f66616eb1bb7085cf")]
    [InlineData("tip", 6, "820082068100", "fd80b9f6d2f00c3a3ee2e9dd125f00b7e9b8a9a1450659a7f2f236d424615ed9")]
    [InlineData("parameters", 5, "820082058103", "db3d61b6341764599c44dd2b3fa5a5eeb715a2725355595052eec428d0c7053b")]
    [InlineData("parameters", 6, "820082068103", "78697eb8345aaeff303bc5ba1af930be40f9f62925b566b87cfea11ab2545409")]
    public void BlockQueryNamesTheGivenEra(string query, int era, string hex, string sha256)
    {
        byte[] golden = Convert.FromHexString(hex);
        Assert.Equal(sha256, Convert.ToHexStringLower(SHA256.HashData(golden)));
        IQueryReq request = query == "tip" ? RawQueries.GetTip((Era)era) : RawQueries.GetCurrentProtocolParams((Era)era);
        byte[] expected = [0x82, 0x00, .. golden];
        Assert.Equal(expected, CborSerializer.Serialize(request));
    }

    // D17: Result_Babbage_LedgerTip and Result_Babbage_EmptyPParams, read through the helpers.
    [Theory]
    [InlineData("tip", "8182095820376ca6b0aff61f1de5bb12326a5c1b9fed98e886274c1bf05144142d5fb7b3a0", "6685ca9dfcafdc837b735408fc678534d671a00715c9d8c22a0eef7a0ccc9be6")]
    [InlineData("parameters", "8196000000190800000000001864d81e820001d81e820001d81e8200018207000000a082d81e820001d81e82000182000082000000189605", "dd1fb4c34cb5358cd8e51877d34e48f68026d21bbbc738911d0c9f03681f26fe")]
    public async Task HelperReadsABabbageAnswer(string helper, string hex, string sha256)
    {
        byte[] golden = Convert.FromHexString(hex);
        Assert.Equal(sha256, Convert.ToHexStringLower(SHA256.HashData(golden)));
        using ProtocolReplay replay = Conversation(helper, 5, [0x82, 0x04, .. golden]);
        await using LocalStateQuery query = new(replay.Channel);
        using CancellationTokenSource deadline = new(TimeSpan.FromSeconds(10));
        CborRecord answer = await B1CarbonGuardTests.InvokeHelper(query, helper, deadline.Token);
        Assert.Equal(golden, answer.Raw.ToArray());
        if (answer is Tip tip)
        {
            Assert.Equal(9UL, Assert.IsType<SpecificPoint>(tip.Slot).Slot);
            return;
        }
        ProtocolParams parameters = Assert.IsType<CurrentProtocolParamsResponse>(answer).ProtocolParams;
        Assert.Equal(7UL, parameters.ProtocolVersion?.Major);
        Assert.Equal(2048UL, parameters.MaxTransactionSize);
        Assert.Equal(150UL, parameters.CollateralPercentage);
        Assert.Equal(5UL, parameters.MaxCollateralInputs);
        Assert.Null(parameters.PoolVotingThresholds);
        Assert.Null(parameters.DRepDeposit);
        Assert.Null(parameters.MinFeeRefScriptCostPerByte);
    }

    [Theory]
    [InlineData("tip", false)]
    [InlineData("address", true)]
    public async Task HelperRefusesAnEraItCannotQuery(string helper, bool unknown)
    {
        // Conway + 1 stays unknown until C5 adds the era and checks its answers; then this row turns red and C5 moves it.
        ulong era = unknown ? (ulong)Era.Conway + 1 : (ulong)Era.Byron;
        using ProtocolReplay replay = Conversation(helper, (int)era);
        replay.CompleteIncoming();
        await using LocalStateQuery query = new(replay.Channel);
        using CancellationTokenSource deadline = new(TimeSpan.FromSeconds(10));
        InvalidOperationException failure = await Assert.ThrowsAsync<InvalidOperationException>(() => B1CarbonGuardTests.InvokeHelper(query, helper, deadline.Token));
        Assert.Equal($"The node's ledger is in era {era}, which these helpers cannot query.", failure.Message);
        Assert.False(query.IsAcquired);
        List<byte[]> sent = replay.ReadSentPayloads();
        Assert.Equal(3, sent.Count);
        Assert.Equal(PreprodFixtureTests.Wire("c4-out-p7-003.cbor"), sent[1]);
        Assert.Equal(PreprodFixtureTests.Wire("c1-out-p7-004.cbor"), sent[2]);
    }

    [Theory]
    [MemberData(nameof(Mismatches))]
    public async Task EraMismatchNamesTheQueryAndLedgerEras(string helper, string hex, string sha256, string queryEra, string ledgerEra)
    {
        byte[] golden = Convert.FromHexString(hex);
        Assert.Equal(sha256, Convert.ToHexStringLower(SHA256.HashData(golden)));
        using ProtocolReplay replay = Conversation(helper, 6, [0x82, 0x04, .. golden]);
        await using LocalStateQuery query = new(replay.Channel);
        using CancellationTokenSource deadline = new(TimeSpan.FromSeconds(10));
        InvalidOperationException failure = await Assert.ThrowsAsync<InvalidOperationException>(() => B1CarbonGuardTests.InvokeHelper(query, helper, deadline.Token));
        Assert.Equal($"Era mismatch: the query is for {queryEra}, but the node's ledger is in {ledgerEra}.", failure.Message);
        Assert.False(query.IsAcquired);
    }

    // SYNTHETIC typed-error replies (exempt from D17): wrong payload type, era with null name,
    // era with three items, the era-mismatch golden sent to the non-era-specific era query, and an integer sent as a system start and as an era history.
    [Theory]
    [InlineData("input", "82048101")]
    [InlineData("input", "8204828201f68200654279726f6e")]
    [InlineData("input", "8204828301675368656c6c65798200654279726f6e8200654279726f6e")]
    [InlineData("era", "8204" + Byron)]
    [InlineData("system-start", "820401")]
    [InlineData("era-history", "820401")]
    public async Task UndecodableResultIsAnInvalidOperation(string helper, string reply)
    {
        using ProtocolReplay replay = Conversation(helper, 6, Convert.FromHexString(reply));
        await using LocalStateQuery query = new(replay.Channel);
        using CancellationTokenSource deadline = new(TimeSpan.FromSeconds(10));
        InvalidOperationException failure = await Assert.ThrowsAsync<InvalidOperationException>(() => B1CarbonGuardTests.InvokeHelper(query, helper, deadline.Token));
        Assert.StartsWith("Failed to deserialize response: ", failure.Message, StringComparison.Ordinal);
        _ = Assert.IsAssignableFrom<CborDecodeException>(failure.InnerException);
        Assert.False(query.IsAcquired);
    }

    // The node's Acquired (c1-in-p7-002); for an era-specific helper the era answer [4, era] (SYNTHETIC, exempt from
    // D17; for era 6 it is the capture c1-in-p7-005); then the given results.
    internal static ProtocolReplay Conversation(string helper, int era, params byte[][] results)
    {
        byte[][] eraAnswer = helper is "era" or "system-start" or "era-history" ? [] : [[0x82, 0x04, (byte)era]];
        return new(ProtocolType.LocalStateQuery, [PreprodFixtureTests.Wire("c1-in-p7-002.cbor"), .. eraAnswer, .. results]);
    }
}
