using System.Security.Cryptography;
using System.Text.Json;
using Chrysalis.Codec.Extensions.Cardano.Core;
using Chrysalis.Codec.Extensions.Cardano.Core.Transaction;
using Chrysalis.Codec.Extensions.Cardano.Core.Common;
using Chrysalis.Codec.Serialization;
using Chrysalis.Codec.Types;
using Chrysalis.Codec.Types.Cardano.Core;
using Chrysalis.Codec.Types.Cardano.Core.Transaction;
using Chrysalis.Network.Cbor.Common;
using Chrysalis.Network.Cbor.Handshake;
using Chrysalis.Codec.Types.Cardano.Core.Protocol;
using Chrysalis.Network.Cbor.LocalStateQuery;
using SAIB.Cbor.Serialization;
using Xunit;
using Chain = Chrysalis.Network.Cbor.ChainSync;
using Monitor = Chrysalis.Network.Cbor.LocalTxMonitor;
using State = Chrysalis.Network.Cbor.LocalStateQuery.Messages;
using Submission = Chrysalis.Network.Cbor.LocalTxSubmit;

namespace Chrysalis.Network.Tests;

public class PreprodFixtureTests
{
    public static IEnumerable<object[]> WireSegments() => Names("wire", "file");
    public static IEnumerable<object[]> Messages() => Names("messages", "id");
    public static IEnumerable<object[]> Blocks() => Names("blocks", "file");
    public static IEnumerable<object[]> Transactions() => Names("transactions", "file");

    [Theory]
    [MemberData(nameof(WireSegments))]
    public void CapturedWireSegmentMatchesPublicCapture(string file)
    {
        JsonElement expected = PreprodFixtures.Find("wire", "file", file);
        _ = AssertIntegrity(expected);
    }

    [Theory]
    [MemberData(nameof(Messages))]
    public void CapturedMessageDecodesAgainstIndependentWireFacts(string id)
    {
        JsonElement expected = PreprodFixtures.Find("messages", "id", id);
        byte[] bytes = PreprodFixtures.Message(expected);
        CborReader reader = new(bytes);
        reader.ReadBeginArray();
        Assert.True(reader.ReadSize() > 0);
        Assert.Equal(expected.GetProperty("index").GetInt32(), reader.ReadInt32());
        CborReader rawReader = new(bytes);
        Assert.Equal(bytes, rawReader.ReadDataItem().ToArray());
        Assert.False(rawReader.DataAvailable);

        // Rows marked with a defect other than B3 validate only the captured term here; the B2 rows' typed decode is
        // InlineQueryPayloadHasIndependentlyRecordedValues.
        if (expected.TryGetProperty("defect", out JsonElement defect) && defect.GetString() != "B3")
        {
            return;
        }

        int index = expected.GetProperty("index").GetInt32();
        switch (expected.GetProperty("protocol").GetInt32())
        {
            case 0:
                AssertHandshake(bytes, expected, index);
                break;
            case 5:
                AssertChainSync(bytes, expected, index);
                break;
            case 6:
                AssertSubmission(bytes, expected, index);
                break;
            case 7:
                AssertStateQuery(bytes, expected, index);
                break;
            case 9:
                AssertMonitor(bytes, expected, index);
                break;
            default:
                Assert.Fail("The capture contains an unexpected mini-protocol.");
                break;
        }
    }

    [Theory]
    [MemberData(nameof(Blocks))]
    public void CapturedBlockMatchesIndependentSlotHeaderHashAndTransactionCount(string file)
    {
        JsonElement expected = PreprodFixtures.Find("blocks", "file", file);
        byte[] bytes = AssertIntegrity(expected);
        BlockWithEra envelope = Decode<BlockWithEra>(bytes);
        Assert.Equal(expected.GetProperty("eraTag").GetInt32(), envelope.EraNumber);
        _ = Assert.IsType<ConwayBlock>(envelope.Block);
        Assert.Equal(expected.GetProperty("slot").GetUInt64(), envelope.Block.Slot());
        Assert.Equal(expected.GetProperty("headerHash").GetString(), envelope.Block.Hash());
        Chain.ChainPoint point = new Chain.ChainSyncHeader(6, null, envelope.Block.Header().Raw).ExtractPoint();
        Assert.Equal(expected.GetProperty("slot").GetUInt64(), point.Slot);
        CborReader headerReader = new(envelope.Block.Header().Raw.Span);
        headerReader.ReadBeginArray();
        _ = headerReader.ReadSize();
        headerReader.ReadBeginArray();
        _ = headerReader.ReadSize();
        Assert.Equal(headerReader.ReadUInt64(), point.BlockNumber);
        Assert.Equal(expected.GetProperty("headerHash").GetString(), Convert.ToHexStringLower(point.Hash));
        Assert.Equal(expected.GetProperty("transactions").GetInt32(), envelope.Block.TransactionBodies().Count());
    }

    [Theory]
    [MemberData(nameof(Transactions))]
    public void ConfirmedTransactionMatchesCardanoCliAndBodyBlake2b256(string file)
    {
        JsonElement expected = PreprodFixtures.Find("transactions", "file", file);
        byte[] bytes = AssertIntegrity(expected);
        PostMaryTransaction transaction = Decode<PostMaryTransaction>(bytes);
        ITransactionBody body = transaction.Body;
        _ = Assert.IsType<ConwayTransactionBody>(body);
        Assert.True(transaction.IsValid);
        Assert.Equal(expected.GetProperty("bodyBytes").GetInt32(), body.Raw.Length);
        Assert.Equal(Convert.FromHexString(expected.GetProperty("txId").GetString()!), Convert.FromHexString(body.Hash()));
        Assert.Equal(expected.GetProperty("fee").GetUInt64(), body.Fee());
        Assert.Equal(expected.GetProperty("inputs").GetInt32(), body.Inputs().Count());
        Assert.Equal(expected.GetProperty("outputs").GetInt32(), body.Outputs().Count());
    }

    [Fact]
    public void RecordedProtocolParametersMatchNodeCliSnapshot()
    {
        JsonElement manifest = PreprodFixtures.Manifest();
        byte[] bytes = AssertIntegrity(manifest.GetProperty("parameters"));
        using JsonDocument document = JsonDocument.Parse(bytes);
        JsonElement parameters = document.RootElement;
        Assert.Equal(31, parameters.EnumerateObject().Count());
        Assert.Equal(11, parameters.GetProperty("protocolVersion").GetProperty("major").GetInt32());
        Assert.Equal(0, parameters.GetProperty("protocolVersion").GetProperty("minor").GetInt32());
        Assert.Equal(44, parameters.GetProperty("txFeePerByte").GetInt32());
        Assert.Equal(155381, parameters.GetProperty("txFeeFixed").GetInt32());
        Assert.Equal(4310, parameters.GetProperty("utxoCostPerByte").GetInt32());
        Assert.Equal(0.0577m, parameters.GetProperty("executionUnitPrices").GetProperty("priceMemory").GetDecimal());
        Assert.Equal(0.0000721m, parameters.GetProperty("executionUnitPrices").GetProperty("priceSteps").GetDecimal());
        JsonElement models = parameters.GetProperty("costModels");
        Assert.Equal(332, models.GetProperty("PlutusV1").GetArrayLength());
        Assert.Equal(332, models.GetProperty("PlutusV2").GetArrayLength());
        Assert.Equal(350, models.GetProperty("PlutusV3").GetArrayLength());
    }

    [Theory]
    [InlineData("c1-in-p7-003.cbor")]
    [InlineData("c1-in-p7-005.cbor")]
    [InlineData("c1-in-p7-007.cbor")]
    [InlineData("c1-in-p7-009.cbor")]
    [InlineData("c1-in-p7-011.cbor")]
    [InlineData("c4-in-p7-003.cbor")]
    public void InlineQueryPayloadHasIndependentlyRecordedValues(string file)
    {
        byte[] bytes = Wire(file);
        State.Result result = Decode<State.Result>(bytes);
        Assert.Equal(4, result.Idx);
        byte[] payload = result.QueryResult.Raw.ToArray();
        Assert.Equal(bytes[2..], payload);
        AssertQueryPayload(file, payload);
        Assert.Equal(bytes, CborSerializer.Serialize(new State.Result(4, new CborAny(payload))));
    }

    [Fact]
    public void FixtureManifestCoversExactlyTheApprovedPublicCorpus()
    {
        JsonElement manifest = PreprodFixtures.Manifest();
        Assert.Equal("Preprod", manifest.GetProperty("network").GetString());
        Assert.Equal(1, manifest.GetProperty("networkMagic").GetInt32());
        Assert.Equal("11.1.3", manifest.GetProperty("nodeVersion").GetString());
        Assert.Equal("2026-11-15", manifest.GetProperty("capturedDate").GetString());
        Assert.Equal(104, manifest.GetProperty("wire").GetArrayLength());
        Assert.Equal(104, manifest.GetProperty("messages").GetArrayLength());
        Assert.Equal(10, manifest.GetProperty("blocks").GetArrayLength());
        Assert.Equal(64, manifest.GetProperty("blocks").EnumerateArray().Sum(block => block.GetProperty("transactions").GetInt32()));
        Assert.Equal(4, manifest.GetProperty("transactions").GetArrayLength());
        string[] resources = [.. typeof(PreprodFixtureTests).Assembly.GetManifestResourceNames()
            .Where(name => name.StartsWith("Chrysalis.Network.Tests.Fixtures.Preprod.", StringComparison.Ordinal))];
        Assert.Equal(118, resources.Count(name => name.EndsWith(".cbor", StringComparison.Ordinal)));
        _ = Assert.Single(resources, name => name.EndsWith(".current-protocol-parameters.json", StringComparison.Ordinal));
    }

    private static IEnumerable<object[]> Names(string collection, string property) =>
        PreprodFixtures.Manifest().GetProperty(collection).EnumerateArray()
            .Select(entry => new object[] { entry.GetProperty(property).GetString()! });

    private static byte[] AssertIntegrity(JsonElement expected)
    {
        byte[] bytes = PreprodFixtures.Read(expected.GetProperty("file").GetString()!);
        Assert.Equal(expected.GetProperty("bytes").GetInt32(), bytes.Length);
        Assert.Equal(expected.GetProperty("sha256").GetString(), Convert.ToHexStringLower(SHA256.HashData(bytes)));
        return bytes;
    }

    internal static byte[] Wire(string file) => AssertIntegrity(PreprodFixtures.Find("wire", "file", "wire/" + file));

    private static T Decode<T>(ReadOnlyMemory<byte> bytes) where T : ICborType
    {
        T result = CborSerializer.Deserialize<T>(bytes, out int consumed);
        Assert.Equal(bytes.Length, consumed);
        return result;
    }

    private static void AssertHandshake(byte[] bytes, JsonElement expected, int index)
    {
        if (index == 0)
        {
            ProposeVersions proposal = Decode<ProposeVersions>(bytes);
            N2CVersionTable table = Assert.IsType<N2CVersionTable>(proposal.VersionTable);
            Assert.Equal(expected.GetProperty("versions").EnumerateArray().Select(version => version.GetInt32()),
                table.Value.Keys.Select(version => version.Value).Order());
            Assert.All(table.Value.Values, data => Assert.Equal(expected.GetProperty("networkMagic").GetUInt64(), data.NetworkMagic));
        }
        else
        {
            N2CAcceptVersion accepted = Decode<N2CAcceptVersion>(bytes);
            Assert.Equal(1, accepted.Idx);
            Assert.Equal(expected.GetProperty("version").GetInt32(), accepted.Version.Value);
            Assert.Equal(expected.GetProperty("networkMagic").GetUInt64(), accepted.VersionData.NetworkMagic);
            Assert.False(accepted.VersionData.Query);
        }
    }

    private static void AssertChainSync(byte[] bytes, JsonElement expected, int index)
    {
        switch (index)
        {
            case 0:
                Assert.Equal(0, Decode<Chain.MessageNextRequest>(bytes).Idx);
                break;
            case 2:
                Chain.N2CMessageRollForward forward = Decode<Chain.N2CMessageRollForward>(bytes);
                Assert.Equal(PreprodFixtures.Read(expected.GetProperty("blockFile").GetString()!), forward.Payload.Value.ToArray());
                AssertTip(expected, forward.Tip);
                break;
            case 3:
                Chain.MessageRollBackward backward = Decode<Chain.MessageRollBackward>(bytes);
                AssertPoint(expected, backward.Point);
                AssertTip(expected, backward.Tip);
                break;
            case 4:
                Chain.MessageFindIntersect intersect = Decode<Chain.MessageFindIntersect>(bytes);
                AssertPoint(expected, Assert.Single(intersect.Points.Value));
                break;
            case 5:
                Chain.MessageIntersectFound found = Decode<Chain.MessageIntersectFound>(bytes);
                AssertPoint(expected, found.Point);
                AssertTip(expected, found.Tip);
                break;
            case 7:
                Assert.Equal(7, Decode<Chain.MessageDone>(bytes).Idx);
                break;
            default:
                Assert.Fail("Unexpected captured chain-sync message.");
                break;
        }
    }

    private static void AssertPoint(JsonElement expected, Point point)
    {
        SpecificPoint specific = Assert.IsType<SpecificPoint>(point);
        Assert.Equal(expected.GetProperty("pointSlot").GetUInt64(), specific.Slot);
        Assert.Equal(expected.GetProperty("pointHash").GetString(), Convert.ToHexStringLower(specific.Hash.Span));
    }

    private static void AssertTip(JsonElement expected, Tip tip)
    {
        SpecificPoint point = Assert.IsType<SpecificPoint>(tip.Slot);
        Assert.Equal(expected.GetProperty("tipSlot").GetUInt64(), point.Slot);
        Assert.Equal(expected.GetProperty("tipHash").GetString(), Convert.ToHexStringLower(point.Hash.Span));
        Assert.Equal(expected.GetProperty("tipBlock").GetInt32(), tip.BlockNumber);
    }

    private static void AssertSubmission(byte[] bytes, JsonElement expected, int index)
    {
        if (index == 0)
        {
            Submission.SubmitTx submit = Decode<Submission.SubmitTx>(bytes);
            Assert.Equal(expected.GetProperty("era").GetInt32(), submit.EraTx.Era);
            Assert.Equal(expected.GetProperty("transactionHex").GetString(), Convert.ToHexStringLower(submit.EraTx.Tx.Value.Span));
        }
        else if (index == 1)
        {
            Assert.Equal(1, Decode<Submission.AcceptTx>(bytes).Idx);
        }
        else
        {
            Submission.RejectTx reject = Decode<Submission.RejectTx>(bytes);
            Assert.Equal(bytes[2..], reject.RejectReason.Raw.ToArray());
            Assert.Equal(bytes, CborSerializer.Serialize(Submission.LocalTxSubmissionMessages.RejectTx(new CborAny(bytes.AsMemory(2)))));
        }
    }

    private static void AssertStateQuery(byte[] bytes, JsonElement expected, int index)
    {
        switch (index)
        {
            case 1:
                Assert.Equal(1, Decode<State.Acquired>(bytes).Idx);
                break;
            case 3:
                State.Query query = Decode<State.Query>(bytes);
                Assert.Equal(expected.GetProperty("queryHex").GetString(), Convert.ToHexStringLower(CborSerializer.Serialize(query.QueryRequest)));
                break;
            case 5:
                Assert.Equal(5, Decode<State.Release>(bytes).Idx);
                break;
            case 7:
                Assert.Equal(7, Decode<State.IsMessageDone>(bytes).Idx);
                break;
            case 8:
                Assert.Equal(8, Decode<State.VolatileTip>(bytes).Idx);
                break;
            default:
                Assert.Fail("Unexpected captured local-state-query message.");
                break;
        }
    }

    private static void AssertMonitor(byte[] bytes, JsonElement expected, int index)
    {
        switch (index)
        {
            case 0:
                Assert.Equal(0, Decode<Monitor.Done>(bytes).Idx);
                break;
            case 1:
                Assert.Equal(1, Decode<Monitor.Acquire>(bytes).Idx);
                break;
            case 2:
                Assert.Equal(expected.GetProperty("slot").GetUInt64(), Decode<Monitor.Acquired>(bytes).Slot);
                break;
            case 3:
                Assert.Equal(3, Decode<Monitor.Release>(bytes).Idx);
                break;
            case 5:
                Assert.Equal(5, Decode<Monitor.NextTx>(bytes).Idx);
                break;
            case 6:
                Assert.Null(Decode<Monitor.ReplyNextTx>(bytes).EraTx);
                break;
            case 8:
                Assert.Equal(expected.GetProperty("hasTx").GetBoolean(), Decode<Monitor.ReplyHasTx>(bytes).HasTx);
                break;
            case 9:
                Assert.Equal(9, Decode<Monitor.GetSizes>(bytes).Idx);
                break;
            case 10:
                Assert.Equal(expected.GetProperty("sizes").EnumerateArray().Select(size => size.GetUInt64()), Decode<Monitor.ReplyGetSizes>(bytes).Sizes);
                break;
            case 11:
                Assert.Equal(11, Decode<Monitor.GetMeasures>(bytes).Idx);
                break;
            default:
                Assert.Fail("Unexpected captured local-tx-monitor message.");
                break;
        }
    }

    internal static void AssertQueryPayload(string file, byte[] payload)
    {
        switch (file)
        {
            case "c1-in-p7-003.cbor":
                Tip tip = Decode<Tip>(payload);
                SpecificPoint point = Assert.IsType<SpecificPoint>(tip.Slot);
                Assert.Equal(139040643UL, point.Slot);
                Assert.Equal("6b39cb68f15c262776b550fb8ac375c2b76e13fad1dfe575d428c8e23c8df515", Convert.ToHexStringLower(point.Hash.Span));
                break;
            case "c1-in-p7-005.cbor":
            case "c4-in-p7-003.cbor":
                Assert.Equal(6UL, Decode<CurrentEraQueryResponse>(payload).CurrentEra);
                break;
            case "c1-in-p7-007.cbor":
                ProtocolParams parameters = Decode<CurrentProtocolParamsResponse>(payload).ProtocolParams;
                Assert.Equal(44UL, parameters.MinFeeA);
                Assert.Equal(155381UL, parameters.MinFeeB);
                Assert.Equal(4310UL, parameters.AdaPerUTxOByte);
                Assert.Equal(11UL, parameters.ProtocolVersion?.Major);
                Assert.Equal(0UL, parameters.ProtocolVersion?.SequenceNumber);
                break;
            case "c1-in-p7-009.cbor":
                UtxoByAddressResponse utxos = Decode<UtxoByAddressResponse>(payload);
                KeyValuePair<TransactionInput, ITransactionOutput> utxo = Assert.Single(utxos.Utxos);
                Assert.Equal("61ca9620c4203111cf895efd1814ef7618777e19ff88f8cca4cfcadb66d46a1f", Convert.ToHexStringLower(utxo.Key.TransactionId.Span));
                Assert.Equal(0UL, utxo.Key.Index);
                Assert.Equal(10000000000UL, utxo.Value.Amount().Lovelace());
                break;
            case "c1-in-p7-011.cbor":
                Assert.Empty(Decode<UtxoByAddressResponse>(payload).Utxos);
                break;
            default:
                Assert.Fail("Unknown query payload fixture.");
                break;
        }
    }
}
