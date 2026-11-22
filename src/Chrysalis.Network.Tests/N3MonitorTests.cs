using System.Security.Cryptography;
using System.Text.Json;
using Chrysalis.Codec.Serialization;
using Chrysalis.Network.Cbor.LocalTxMonitor;
using Chrysalis.Network.Core;
using Chrysalis.Network.MiniProtocols;
using Xunit;

namespace Chrysalis.Network.Tests;

// D17 provenance (Apache-2.0): ouroboros-consensus release-ouroboros-consensus-5.4.0.0 (808e4005),
// ouroboros-consensus-cardano/golden/cardano/QueryVersion3/CardanoNodeToClientVersion19/GenTxId_Conway, GenTxId_Babbage and GenTx_Conway;
// MsgHasTx and MsgReplyNextTx framing from ouroboros-network LocalTxMonitor/Codec.hs:74-75,91-92.
public sealed class N3MonitorTests
{
    private const string ConwayTxId = "820658203da9a3b38dce0e87ca4a88f3328caac7a970cf9ac40424cde2b675768d11f11b";
    private const string BabbageTxId = "8205582036fdff68dfe3660f1ceea60f018a0fd7a83da13def229108794c397a879b0436";
    private const string ConwayTx = "8206d8184e84a300d901028001800200a0f5f6";

    [Fact]
    public async Task ReleaseReturnsWithoutAReplyAndTheConnectionAcquiresAgain()
    {
        // The node's two Acquired replies (c3, c8); the frames sent are compared with cardano-cli's (c5).
        using ProtocolReplay replay = new(ProtocolType.LocalTxMonitor, PreprodFixtureTests.Wire("c3-in-p9-002.cbor"), PreprodFixtureTests.Wire("c8-in-p9-002.cbor"));
        replay.CompleteIncoming();
        LocalTxMonitor monitor = new(replay.Channel);
        using CancellationTokenSource deadline = new(TimeSpan.FromSeconds(10));
        _ = Assert.IsType<Acquired>(await monitor.AcquireAsync(deadline.Token));
        Assert.Equal(PreprodFixtureTests.Wire("c5-out-p9-002.cbor"), replay.ReadSentPayload());
        Task release = monitor.ReleaseAsync(deadline.Token);
        await release;
        Assert.Equal(PreprodFixtures.Message(PreprodFixtures.Find("messages", "id", "c5-out-p9-004")), replay.ReadSentPayload());
        Acquired again = Assert.IsType<Acquired>(await monitor.AcquireAsync(deadline.Token));
        Assert.Equal(PreprodFixtures.Find("messages", "id", "c8-in-p9-002").GetProperty("slot").GetUInt64(), again.Slot);
        Assert.Equal(PreprodFixtureTests.Wire("c5-out-p9-002.cbor"), replay.ReadSentPayload());
    }

    [Fact]
    public async Task ReleaseWithACancelledTokenThrowsAndSendsNothing()
    {
        using ProtocolReplay replay = new(ProtocolType.LocalTxMonitor);
        LocalTxMonitor monitor = new(replay.Channel);
        _ = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => monitor.ReleaseAsync(new CancellationToken(canceled: true)));
        replay.AssertNothingSent();
    }

    [Fact]
    public async Task HasTxSendsTheIdAsCardanoCliDoes()
    {
        JsonElement request = PreprodFixtures.Find("messages", "id", "c5-out-p9-003");
        byte[] reply = PreprodFixtureTests.Wire("c5-in-p9-003.cbor");
        using ProtocolReplay replay = new(ProtocolType.LocalTxMonitor, reply, reply);
        LocalTxMonitor monitor = new(replay.Channel);
        using CancellationTokenSource deadline = new(TimeSpan.FromSeconds(10));
        string txId = request.GetProperty("txId").GetString()!;
        Assert.False(Assert.IsType<ReplyHasTx>(await monitor.HasTxAsync(txId, deadline.Token)).HasTx);
        Assert.Equal(PreprodFixtureTests.Wire("c5-out-p9-003.cbor"), replay.ReadSentPayload());
        EraTxId typed = new(request.GetProperty("era").GetInt32(), Convert.FromHexString(txId));
        Assert.False(Assert.IsType<ReplyHasTx>(await monitor.HasTxAsync(typed, deadline.Token)).HasTx);
        Assert.Equal(PreprodFixtureTests.Wire("c5-out-p9-003.cbor"), replay.ReadSentPayload());
    }

    [Fact]
    public void GoldenTxIdIsWrittenByBothFactories()
    {
        byte[] golden = Convert.FromHexString(ConwayTxId);
        Assert.Equal("ca25a3ad64dae25aaec162f18edcefb110a441b8321a57066844ab2e89445e80", Convert.ToHexStringLower(SHA256.HashData(golden)));
        byte[] message = [0x82, 0x07, .. golden];
        Assert.Equal(message, CborSerializer.Serialize(LocalTxMonitorMessages.HasTx(ConwayTxId[8..])));
        Assert.Equal(message, CborSerializer.Serialize(LocalTxMonitorMessages.HasTx(new EraTxId(6, golden.AsMemory(4)))));
        EraTxId decoded = Assert.IsType<HasTx>(CborSerializer.Deserialize<LocalTxMonitorMessage>(message)).TxId;
        Assert.Equal(6, decoded.Era);
        Assert.Equal(golden[4..], decoded.Hash.ToArray());
        byte[] babbage = Convert.FromHexString(BabbageTxId);
        Assert.Equal("9b736920211fd5d9b61ec4375358f8bf09fc19de53bb2ff31b4b4df4706038cd", Convert.ToHexStringLower(SHA256.HashData(babbage)));
        byte[] babbageMessage = [0x82, 0x07, .. babbage];
        Assert.Equal(babbageMessage, CborSerializer.Serialize(LocalTxMonitorMessages.HasTx(new EraTxId(5, babbage.AsMemory(4)))));
    }

    // SYNTHETIC ids, none 64 characters long, so none gives 32 bytes either way; each is refused before anything is sent.
    [Theory]
    [InlineData("")]
    [InlineData("3da9a3b38dce0e87ca4a88f3328caac7a970cf9ac40424cde2b675768d11f1")] // 31 bytes
    [InlineData("3da9a3b38dce0e87ca4a88f3328caac7a970cf9ac40424cde2b675768d11f11b00")] // 33 bytes
    [InlineData("3da9a3b38dce0e87ca4a88f3328caac7a970cf9ac40424cde2b675768d11f11")] // 63 digits
    [InlineData("0x3da9a3b38dce0e87ca4a88f3328caac7a970cf9ac40424cde2b675768d11")] // not hexadecimal
    public async Task MalformedIdIsRefusedBeforeAnythingIsSent(string txId)
    {
        using ProtocolReplay replay = new(ProtocolType.LocalTxMonitor);
        replay.CompleteIncoming();
        LocalTxMonitor monitor = new(replay.Channel);
        _ = await Assert.ThrowsAsync<ArgumentException>(() => monitor.HasTxAsync(txId, CancellationToken.None));
        _ = await Assert.ThrowsAsync<ArgumentException>(() => monitor.HasTxAsync(new EraTxId(6, new byte[txId.Length / 2]), CancellationToken.None));
        replay.AssertNothingSent();
    }

    [Fact]
    public void GoldenTransactionReadsAsTheNextTx()
    {
        byte[] golden = Convert.FromHexString(ConwayTx);
        Assert.Equal("b55e9d34639422a76ead39d8278388a5d478e4d0a8aa876795707057f4038614", Convert.ToHexStringLower(SHA256.HashData(golden)));
        byte[] message = [0x82, 0x06, .. golden];
        ReplyNextTx next = Assert.IsType<ReplyNextTx>(CborSerializer.Deserialize<LocalTxMonitorMessage>(message));
        Assert.NotNull(next.EraTx);
        Assert.Equal(6, next.EraTx.Era);
        Assert.Equal(golden[5..], next.EraTx.Tx.Value.ToArray());
    }
}
