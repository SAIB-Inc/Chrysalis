using System.Globalization;
using System.Numerics;
using System.Security.Cryptography;
using System.Text.Json;
using Chrysalis.Codec.Serialization;
using Chrysalis.Codec.Types;
using Chrysalis.Codec.Types.Cardano.Core.Common;
using Chrysalis.Network.Cbor.LocalStateQuery;
using Chrysalis.Network.MiniProtocols;
using Xunit;

namespace Chrysalis.Network.Tests;

// D17 provenance (Apache-2.0). ouroboros-consensus release-ouroboros-consensus-5.4.0.0 (808e4005),
// ouroboros-consensus-cardano/golden/cardano/QueryVersion3/CardanoNodeToClientVersion19/: Query_HardFork; Result_HardFork
// (four-field bounds, as node 11.2.0 and later send); Result_Conway_GenesisConfig, whose bytes 2 to 8 are a UTC time written
// by cardano-ledger-binary's encodeUTCTime (Encoder.hs:549-557 at 1d78754a), the layout of the system-start result
// (Ledger/Query.hs:520-521; Pallas v1.5.0-rc.2 queries_v16/mod.rs:311-320); no golden holds that result itself.
// cardano-cli 11.7.0.0 (24c05829), cardano-cli/test/cardano-cli-test/files/input/preview-era-history.json (three-field
// bounds, as node 11.1.3 sends, with bignum times), linked from Chrysalis.Tx.Test/TestData/cli. The system-start request [1]
// is read from ouroboros-consensus Ledger/Query.hs:428-432; the devnet test checks it against a node.
public sealed class N6EraHistoryTests
{
    private const string QueryHardFork = "82028100";
    private const string ResultHardFork = "9f838400000000841b0018de76816d80001903e80a00841864191b5883001481001483841b0018de76816d80001903e80a00841b0019945ca262000019044c0b008418641907d08300182281001822ff";
    private const string GenesisConfig = "818f831907e4188700000082090a0a1864140a1a001e8480051b009fdf42f6e4800091000000190800000000001864d81e820001d81e820001d81e820001d81e82000181008202000000a0a082a0a0";
    private const string SystemStart = "831907e4188700";

    [Theory]
    [InlineData("system-start", "8101", SystemStart)]
    [InlineData("era-history", "8200" + QueryHardFork, ResultHardFork)]
    public async Task HelperSendsItsQueryAndReturnsTheResult(string helper, string request, string result)
    {
        byte[] reply = [0x82, 0x04, .. Convert.FromHexString(result)];
        using ProtocolReplay replay = N1QueryResultTests.Conversation(helper, 6, reply);
        await using LocalStateQuery query = new(replay.Channel);
        using CancellationTokenSource deadline = new(TimeSpan.FromSeconds(10));
        CborRecord answer = await B1CarbonGuardTests.InvokeHelper(query, helper, deadline.Token);
        Assert.Equal(reply[2..], answer.Raw.ToArray());
        Assert.False(query.IsAcquired);
        // Acquire, the query in MsgQuery [3, query] with no era query before it, release.
        List<byte[]> sent = replay.ReadSentPayloads();
        Assert.Equal(3, sent.Count);
        Assert.Equal(PreprodFixtureTests.Wire("c1-out-p7-002.cbor"), sent[0]);
        byte[] expected = [0x82, 0x03, .. Convert.FromHexString(request)];
        Assert.Equal(expected, sent[1]);
        Assert.Equal(PreprodFixtureTests.Wire("c1-out-p7-004.cbor"), sent[2]);
    }

    [Fact]
    public void ConsensusEraHistoryHasFourFieldBounds()
    {
        _ = Golden(QueryHardFork, "df741c1ff4879466733075febe722efda2557e7bd986e3dc3604384c13f658da");
        EraHistoryResponse history = CborSerializer.Deserialize<EraHistoryResponse>(Golden(ResultHardFork, "0272ddaabd13d9f35bda08fe163499bd4a11d647497b374a708863422712a69c"));
        Assert.Equal(2, history.Summaries.Count);
        AssertBound(history.Summaries[0].Start, 0, 0, 0, 0);
        AssertBound(history.Summaries[0].End, 7_000_000_000_000_000, 1000, 10, 0);
        AssertParameters(history.Summaries[0].Parameters, 100, 7000, 20);
        AssertBound(history.Summaries[1].Start, 7_000_000_000_000_000, 1000, 10, 0);
        AssertBound(history.Summaries[1].End, 7_200_000_000_000_000, 1100, 11, 0);
        AssertParameters(history.Summaries[1].Parameters, 100, 2000, 34);
    }

    [Fact]
    public void CliEraHistoryHasThreeFieldBoundsAndBignumTimes()
    {
        using Stream resource = typeof(N6EraHistoryTests).Assembly.GetManifestResourceStream("Cli/preview-era-history.json")
            ?? throw new InvalidOperationException("Missing the cardano-cli era history.");
        using MemoryStream file = new();
        resource.CopyTo(file);
        Assert.Equal("737240464c5a780b5c4b6b92775e178f0cca06afd281d38c05f060c97ef0c35a", Convert.ToHexStringLower(SHA256.HashData(file.ToArray())));
        using JsonDocument envelope = JsonDocument.Parse(file.ToArray());
        EraHistoryResponse history = CborSerializer.Deserialize<EraHistoryResponse>(Convert.FromHexString(envelope.RootElement.GetProperty("cborHex").GetString()!));
        Assert.Equal(7, history.Summaries.Count);
        AssertBound(history.Summaries[0].End, 0, 0, 0, null);
        AssertParameters(history.Summaries[0].Parameters, 4320, 20000, 864);
        AssertBound(history.Summaries[4].End, 259_200_000_000_000_000, 259_200, 3, null);
        BigInteger conway = BigInteger.Parse("55814400000000000000", CultureInfo.InvariantCulture);
        AssertBound(history.Summaries[5].End, conway, 55_814_400, 646, null);
        AssertBound(history.Summaries[6].Start, conway, 55_814_400, 646, null);
        AssertBound(history.Summaries[6].End, BigInteger.Parse("74476800000000000000", CultureInfo.InvariantCulture), 74_476_800, 862, null);
        AssertParameters(history.Summaries[6].Parameters, 86_400, 1000, 25_920);
    }

    // SYNTHETIC (exempt from D17): an open end, the indefinite safe zone, a round number and a round length, which no
    // golden holds; a negative time, which no node sends; and two bounds built in code, whose time is a signed integer.
    [Fact]
    public void OpenEndIndefiniteSafeZoneAndPerasFieldsAreRead()
    {
        EraSummary summary = Assert.Single(CborSerializer.Deserialize<EraHistoryResponse>(Convert.FromHexString("9f838400000005f68518641903e8810114185aff")).Summaries);
        Assert.Null(summary.End);
        Assert.Equal(5UL, summary.Start.NextPerasRound);
        _ = Assert.IsType<IndefiniteSafeZone>(summary.Parameters.SafeZone);
        Assert.Equal(90UL, summary.Parameters.PerasRoundLength);
        Assert.Null(EraSummary.Create(summary.Start, summary.Parameters).End);
        EraBound negative = CborSerializer.Deserialize<EraBound>(Convert.FromHexString("83200000"));
        _ = Assert.Throws<InvalidOperationException>(() => { _ = negative.TimePicoseconds; });
        AssertBound(new EraBound(PlutusInt.Create(7), 0, 0, null), 7, 0, 0, null);
        AssertBound(new EraBound(PlutusInt64.Create(7), 0, 0, null), 7, 0, 0, null);
    }

    // The first row is the system start in Result_Conway_GenesisConfig. The second is SYNTHETIC (exempt from D17): half a
    // second past noon plus 99,999 picoseconds, which the UTC value drops.
    [Theory]
    [InlineData(SystemStart, "2020-05-14T00:00:00.0000000+00:00")]
    [InlineData("831907e919016d1b00997aa038a00e9f", "2025-12-31T12:00:00.5000000+00:00")]
    public void SystemStartIsReadAsUtc(string hex, string expected)
    {
        Assert.Equal(SystemStart, Convert.ToHexStringLower(Golden(GenesisConfig, "5f89d0d14b2025f4a1636a3eacacb9cca27245517006640675e8ce0b2b6d6cb9").AsSpan(2, 7)));
        SystemStartResponse start = CborSerializer.Deserialize<SystemStartResponse>(Convert.FromHexString(hex));
        Assert.Equal(expected, start.Utc.ToString("O", CultureInfo.InvariantCulture));
    }

    private static byte[] Golden(string hex, string sha256)
    {
        byte[] bytes = Convert.FromHexString(hex);
        Assert.Equal(sha256, Convert.ToHexStringLower(SHA256.HashData(bytes)));
        return bytes;
    }

    private static void AssertBound(EraBound? actual, BigInteger picoseconds, ulong slot, ulong epoch, ulong? nextPerasRound)
    {
        EraBound bound = Assert.IsType<EraBound>(actual);
        Assert.Equal(picoseconds, bound.TimePicoseconds);
        Assert.Equal(slot, bound.Slot);
        Assert.Equal(epoch, bound.Epoch);
        Assert.Equal(nextPerasRound, bound.NextPerasRound);
    }

    // In every vector the safe zone and the Genesis window hold the same number of slots.
    private static void AssertParameters(EraParams parameters, ulong epochSize, ulong slotLength, ulong slots)
    {
        Assert.Equal(epochSize, parameters.EpochSize);
        Assert.Equal(slotLength, parameters.SlotLengthMilliseconds);
        Assert.Equal(slots, Assert.IsType<StandardSafeZone>(parameters.SafeZone).Slots);
        Assert.Equal(slots, parameters.GenesisWindow);
        Assert.Null(parameters.PerasRoundLength);
    }
}
