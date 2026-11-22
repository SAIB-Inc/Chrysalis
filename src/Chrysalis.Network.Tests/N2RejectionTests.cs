using System.Security.Cryptography;
using Chrysalis.Codec.Serialization;
using Chrysalis.Codec.Types;
using Chrysalis.Network.Cbor.LocalTxSubmit;
using Chrysalis.Network.Core;
using Chrysalis.Network.MiniProtocols;
using Xunit;

namespace Chrysalis.Network.Tests;

// D17 provenance (Apache-2.0): ouroboros-consensus release-ouroboros-consensus-5.4.0.0 (808e4005),
// ouroboros-consensus-cardano/golden/cardano/QueryVersion3/CardanoNodeToClientVersion19/ApplyTxErr_Conway;
// ApplyTxErr_WrongEraByron and _WrongEraShelley hold the bytes of N1QueryResultTests.Byron and .Shelley.
// MsgRejectTx framing from ouroboros-network LocalTxSubmission/Codec.hs:121-124.
public sealed class N2RejectionTests
{
    private const string Conway = "818206818202820182018206581ce0a714319812c3f773ba04ec5d6b3ffcd5aad85006805b047b082541";

    [Fact]
    public async Task SubmitReturnsTheNodesRejectionAndStaysInStep()
    {
        byte[] reply = PreprodFixtureTests.Wire("c10-in-p6-002.cbor");
        using ProtocolReplay replay = new(ProtocolType.LocalTxSubmission, reply, PreprodFixtureTests.Wire("c11-in-p6-002.cbor"));
        LocalTxSubmit submit = new(replay.Channel);
        using CancellationTokenSource deadline = new(TimeSpan.FromSeconds(10));
        SubmitTx transaction = LocalTxSubmissionMessages.SubmitTx(new EraTx(6, new CborEncodedValue(Convert.FromHexString("84a300d901028001800200a0f5f6"))));
        RejectTx rejection = Assert.IsType<RejectTx>(await submit.SubmitTxAsync(transaction, deadline.Token));
        Assert.Equal(PreprodFixtureTests.Wire("c10-out-p6-002.cbor"), replay.ReadSentPayload());
        Assert.Equal(reply[2..], rejection.RejectReason.Raw.ToArray());
        Assert.Equal("Rejected in era 6: [[7, \"All inputs are spent. Transaction has probably already been included\"]]", rejection.Describe());
        _ = Assert.IsType<AcceptTx>(await submit.SubmitTxAsync(transaction, deadline.Token));
    }

    [Theory]
    [InlineData(Conway, "d57c0f43771b4cd3d7033d727d91a146877f8960969b35524cb470bbfb23503b", "Rejected in era 6: [[2, [1, [1, [6, h'e0a714319812c3f773ba04ec5d6b3ffcd5aad85006805b047b082541']]]]]")]
    [InlineData(N1QueryResultTests.Byron, "db0b7e1431611d06652fa74798465303e2128ffd6090ee78f9fbeb9a7695ff06", "Era mismatch: the transaction is for Shelley (1), but the node's ledger is in Byron (0).")]
    [InlineData(N1QueryResultTests.Shelley, "bdcc7be632b8438ad6d58f4e3b0cd629da8244b2be6aa896115ee2c007be2c31", "Era mismatch: the transaction is for Byron (0), but the node's ledger is in Shelley (1).")]
    public void GoldenRejectionDecodesAndReads(string hex, string sha256, string expected)
    {
        byte[] reason = Convert.FromHexString(hex);
        Assert.Equal(sha256, Convert.ToHexStringLower(SHA256.HashData(reason)));
        byte[] message = [0x82, 0x02, .. reason];
        RejectTx rejection = Assert.IsType<RejectTx>(CborSerializer.Deserialize<LocalTxSubmissionMessage>(message));
        Assert.Equal(reason, rejection.RejectReason.Raw.ToArray());
        Assert.Equal(expected, rejection.Describe());
    }

    // RFC 8949 Appendix A, Table 6, as written there, except the last two rows (SYNTHETIC): text with control
    // characters, and one item holding a pair whose first item is not a one-byte era index.
    [Theory]
    [InlineData("8301820203820405", "[1, [2, 3], [4, 5]]")]
    [InlineData("a26161016162820203", "{" + "\"a\": 1, \"b\": [2, 3]}")]
    [InlineData("9f018202039f0405ffff", "[_ 1, [2, 3], [_ 4, 5]]")]
    [InlineData("bf6346756ef563416d7421ff", "{_ \"Fun\": true, \"Amt\": -2}")]
    [InlineData("9fff", "[_ ]")]
    [InlineData("c11a514b67b0", "1(1363896240)")]
    [InlineData("d818456449455446", "24(h'6449455446')")]
    [InlineData("62225c", "\"\\\"\\\\\"")]
    [InlineData("1bffffffffffffffff", "18446744073709551615")]
    [InlineData("3903e7", "-1000")]
    [InlineData("f4", "false")]
    [InlineData("f6", "null")]
    [InlineData("f7", "undefined")]
    [InlineData("83019f0203ff820405", "[1, [_ 2, 3], [4, 5]]")]
    [InlineData("a0", "{}")]
    [InlineData("6461090a01", "\"a\\t\\n\\u0001\"")]
    [InlineData("8182181880", "[[24, []]]")]
    public void OtherReasonsReadInDiagnosticNotation(string hex, string expected) =>
        Assert.Equal(expected, LocalTxSubmissionMessages.RejectTx(new CborAny(Convert.FromHexString(hex))).Describe());

    // Shown whole as hex. SYNTHETIC rows (PROGRAM §5's exemption for typed-error inputs, as N1's T4), but the last two
    // are RFC 8949 Table 6's -18446744073709551616 and (_ "strea", "ming").
    [Theory]
    [InlineData("828201f68200654279726f6e")] // an era without a name
    [InlineData("828200654279726f6e82017f475368656c6c6579ff")] // an era mismatch with a malformed indefinite text string
    [InlineData("828200654279726f6e8201675368656c6c657900")] // an era mismatch with trailing bytes
    [InlineData("818206f93e00")] // a float among the era's errors
    [InlineData("8182067fff")] // an empty indefinite-length text string among the era's errors
    [InlineData("f818")] // a simple value below 32 in two bytes, not well-formed (RFC 8949 §3.3)
    [InlineData("fc00000000000000000000000000000000")] // 0xFC, reserved
    [InlineData("ff")] // a lone break
    [InlineData("f8")] // cut off
    [InlineData("9f01")] // an indefinite list cut off
    [InlineData("3bffffffffffffffff")]
    [InlineData("7f657374726561646d696e67ff")]
    public void UnshownReasonIsGivenWholeAsHex(string hex) =>
        Assert.Equal($"h'{hex}'", LocalTxSubmissionMessages.RejectTx(new CborAny(Convert.FromHexString(hex))).Describe());

    // SYNTHETIC: 64 nested one-item lists are shown; 65 are deeper than the limit and shown whole as hex.
    [Fact]
    public void ReasonsNestUpTo64Levels()
    {
        byte[] deep = [.. Enumerable.Repeat((byte)0x81, 65), 0x00];
        Assert.Equal($"{new string('[', 64)}0{new string(']', 64)}", LocalTxSubmissionMessages.RejectTx(new CborAny(deep.AsMemory(1))).Describe());
        Assert.Equal($"h'{Convert.ToHexStringLower(deep)}'", LocalTxSubmissionMessages.RejectTx(new CborAny(deep)).Describe());
    }
}
