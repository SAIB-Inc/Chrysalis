using System.Security.Cryptography;
using Chrysalis.Codec.Serialization;
using Chrysalis.Network.Cbor.Common;
using Chrysalis.Network.Cbor.LocalStateQuery;
using Chrysalis.Network.Cbor.LocalStateQuery.Messages;
using Chrysalis.Network.Core;
using Chrysalis.Network.MiniProtocols;
using Xunit;

namespace Chrysalis.Network.Tests;

// D17 provenance (Apache-2.0): 820201 from gouroboros v0.220.2 (a2926d95dfd43244e99097d0240069088b4366cc,
// protocol/localstatequery/messages_test.go:53); 820200 derived from ouroboros-network 34831d62d
// (ouroboros-network/protocols/lib/Ouroboros/Network/Protocol/LocalStateQuery/Codec.hs:63-73,102-105;
// cardano-diffusion/protocols/cddl/specs/local-state-query.cddl:15-19,28); agreeing reading (no bytes taken):
// Pallas v1.5.0-rc.2 (daecef59, pallas-network/src/miniprotocols/localstate/codec.rs:5-34).
public sealed class AcqFailReasonTests
{
    [Theory]
    [InlineData("820200", "95397da2338a6e41e75d2a34be802255e05cb6e7d79088fcd6a0d9f99b344681", typeof(AcquireFailurePointTooOld), 0)]
    [InlineData("820201", "67a858f98fb948cd6f96601d4b02eaa254efb430f80ed19766c785096297f05e", typeof(AcquireFailurePointNotOnChain), 1)]
    public void MsgFailureDecodesAndRoundTripsBareIntegerReason(string hex, string sha256, Type expectedReason, int expectedCode)
    {
        byte[] bytes = Convert.FromHexString(hex);
        Assert.Equal(sha256, Convert.ToHexStringLower(SHA256.HashData(bytes)));

        LocalStateQueryMessage message = CborSerializer.Deserialize<LocalStateQueryMessage>(bytes, out int consumed);
        Assert.Equal(bytes.Length, consumed);
        Failure failure = Assert.IsType<Failure>(message);
        Assert.Equal(2, failure.Idx);
        Assert.IsType(expectedReason, failure.Reason);
        Assert.Equal(bytes[2..], failure.Reason.Raw.ToArray());

        FailureReason freshReason = expectedCode == 0
            ? new AcquireFailurePointTooOld(0)
            : new AcquireFailurePointNotOnChain(1);
        Assert.Equal(bytes, CborSerializer.Serialize(new Failure(2, freshReason)));
    }

    [Theory]
    [InlineData("820200", nameof(AcquireFailurePointTooOld))]
    [InlineData("820201", nameof(AcquireFailurePointNotOnChain))]
    public async Task AcquireAsyncThrowsInvalidOperationNamingReasonAndKeepsBufferInStep(string hex, string expectedReasonName)
    {
        byte[] coalesced = [.. Convert.FromHexString(hex), .. PreprodFixtureTests.Wire("c1-in-p7-002.cbor")];
        using ProtocolReplay replay = new(ProtocolType.LocalStateQuery, coalesced);
        await using LocalStateQuery query = new(replay.Channel);
        using CancellationTokenSource deadline = new(TimeSpan.FromSeconds(10));

        InvalidOperationException failure = await Assert.ThrowsAsync<InvalidOperationException>(
            () => query.AcquireAsync(Point.Specific(1UL, new byte[32]), deadline.Token));
        Assert.Equal($"Failed to acquire state: {expectedReasonName}.", failure.Message);
        Assert.False(query.IsAcquired);

        await query.AcquireAsync(null, deadline.Token);
        Assert.True(query.IsAcquired);
    }

    // SYNTHETIC malformed/unknown inputs (exempt from D17): unknown code 2, negative -1,
    // legacy list forms [2, [0]] and [2, [1]], and semantic-tagged integer tag(24, 0).
    [Theory]
    [InlineData("820202")]
    [InlineData("820220")]
    [InlineData("82028100")]
    [InlineData("82028101")]
    [InlineData("8202d81800")]
    public void MalformedOrUnknownFailureReasonThrowsCborDecodeException(string hex) =>
        _ = Assert.ThrowsAny<CborDecodeException>(() =>
            CborSerializer.Deserialize<LocalStateQueryMessage>(Convert.FromHexString(hex)));

    [Fact]
    public void EmptyFailureReasonThrowsCborDecodeException()
    {
        CborDecodeException error = Assert.Throws<CborDecodeException>(() => FailureReason.Read(ReadOnlyMemory<byte>.Empty, out _));
        Assert.Equal(0, error.Offset);
        Assert.Null(error.InnerException);
    }

    [Fact]
    public void ConcreteReasonRejectsWrongCodeOnReadAndWrite()
    {
        _ = Assert.Throws<CborValidationException>(() => CborSerializer.Deserialize<AcquireFailurePointTooOld>(new byte[] { 0x01 }));
        _ = Assert.Throws<CborValidationException>(() => CborSerializer.Deserialize<AcquireFailurePointNotOnChain>(new byte[] { 0x00 }));
        _ = Assert.Throws<CborValidationException>(() => CborSerializer.Serialize(new AcquireFailurePointTooOld(1)));
        _ = Assert.Throws<CborValidationException>(() => CborSerializer.Serialize(new AcquireFailurePointNotOnChain(0)));
    }
}
