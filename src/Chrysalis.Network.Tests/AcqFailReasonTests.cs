using System.Security.Cryptography;
using System.Text.Json;
using Chrysalis.Codec.Serialization;
using Chrysalis.Network.Cbor.Common;
using Chrysalis.Network.Cbor.LocalStateQuery;
using Chrysalis.Network.Cbor.LocalStateQuery.Messages;
using Chrysalis.Network.Core;
using Chrysalis.Network.MiniProtocols;
using Xunit;

namespace Chrysalis.Network.Tests;

public sealed class AcqFailReasonTests
{
    [Theory]
    [InlineData("acquire-failure-too-old.cbor", "95397da2338a6e41e75d2a34be802255e05cb6e7d79088fcd6a0d9f99b344681", typeof(AcquireFailurePointTooOld), 0)]
    [InlineData("acquire-failure-not-on-chain.cbor", "67a858f98fb948cd6f96601d4b02eaa254efb430f80ed19766c785096297f05e", typeof(AcquireFailurePointNotOnChain), 1)]
    public void MsgFailureDecodesAndRoundTripsBareIntegerReason(string fixture, string sha256, Type expectedReason, int expectedCode)
    {
        ArgumentNullException.ThrowIfNull(expectedReason);
        (byte[] bytes, JsonElement expected) = Capture(fixture, sha256);
        Assert.Equal(expectedReason.Name, expected.GetProperty("reason").GetString());
        Assert.Equal(expectedCode, expected.GetProperty("code").GetInt32());

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
    [InlineData("acquire-failure-too-old.cbor", nameof(AcquireFailurePointTooOld), false)]
    [InlineData("acquire-failure-not-on-chain.cbor", nameof(AcquireFailurePointNotOnChain), false)]
    [InlineData("acquire-failure-too-old.cbor", nameof(AcquireFailurePointTooOld), true)]
    [InlineData("acquire-failure-not-on-chain.cbor", nameof(AcquireFailurePointNotOnChain), true)]
    public async Task AcquireAsyncThrowsInvalidOperationNamingReasonAndKeepsBufferInStep(string fixture, string expectedReasonName, bool acquiredFirst)
    {
        byte[] acquired = PreprodFixtureTests.Wire("c1-in-p7-002.cbor");
        byte[] failureBytes = Capture(fixture, null).Bytes;
        byte[] coalesced = acquiredFirst
            ? [.. acquired, .. failureBytes, .. acquired]
            : [.. failureBytes, .. acquired];
        using ProtocolReplay replay = new(ProtocolType.LocalStateQuery, coalesced);
        await using LocalStateQuery query = new(replay.Channel);
        using CancellationTokenSource deadline = new(TimeSpan.FromSeconds(10));

        if (acquiredFirst)
        {
            await query.AcquireAsync(null, deadline.Token);
            Assert.True(query.IsAcquired);
        }

        InvalidOperationException failure = await Assert.ThrowsAsync<InvalidOperationException>(
            () => query.AcquireAsync(Point.Specific(1UL, new byte[32]), deadline.Token));
        Assert.Equal($"Failed to acquire state: {expectedReasonName}.", failure.Message);
        Assert.False(query.IsAcquired);

        await query.AcquireAsync(null, deadline.Token);
        Assert.True(query.IsAcquired);
    }

    // SYNTHETIC unexpected-reply input (exempt from D17): out-of-agency Query [3, [1]] in StAcquiring.
    [Fact]
    public async Task AcquireAsyncUnexpectedReplyWhenAcquiredClearsIsAcquired()
    {
        byte[] coalesced = [.. PreprodFixtureTests.Wire("c1-in-p7-002.cbor"), .. Convert.FromHexString("82038101")];
        using ProtocolReplay replay = new(ProtocolType.LocalStateQuery, coalesced);
        await using LocalStateQuery query = new(replay.Channel);
        using CancellationTokenSource deadline = new(TimeSpan.FromSeconds(10));

        await query.AcquireAsync(null, deadline.Token);
        Assert.True(query.IsAcquired);

        InvalidOperationException error = await Assert.ThrowsAsync<InvalidOperationException>(
            () => query.AcquireAsync(null, deadline.Token));
        Assert.StartsWith("Failed to acquire state:", error.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(nameof(AcquireFailurePointTooOld), error.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(nameof(AcquireFailurePointNotOnChain), error.Message, StringComparison.Ordinal);
        Assert.False(query.IsAcquired);
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

    private static (byte[] Bytes, JsonElement Expected) Capture(string name, string? pinnedSha256)
    {
        using JsonDocument manifest = JsonDocument.Parse(PreprodFixtures.Read("LocalStateQuery", "manifest.json"));
        JsonElement entry = manifest.RootElement.GetProperty("captures").GetProperty(name);
        byte[] bytes = PreprodFixtures.Read("LocalStateQuery", name);
        string expectedSha = entry.GetProperty("sha256").GetString()!;
        Assert.Equal(entry.GetProperty("bytes").GetInt32(), bytes.Length);
        if (pinnedSha256 is not null)
        {
            Assert.Equal(pinnedSha256, expectedSha);
        }
        Assert.Equal(expectedSha, Convert.ToHexStringLower(SHA256.HashData(bytes)));
        return (bytes, entry.GetProperty("expected").Clone());
    }
}
