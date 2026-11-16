using Chrysalis.Codec.Serialization;
using Chrysalis.Codec.Types;
using Chrysalis.Network.Cbor.Handshake;
using Chrysalis.Network.Core;
using Chrysalis.Network.MiniProtocols;
using Xunit;

namespace Chrysalis.Network.Tests;

/// <summary>Malformed handshake data must be decode failures, not negotiated state or typed node refusals.</summary>
public sealed class B1CarbonShapeTests
{
    /// <summary>N2C data always contains a boolean query field.</summary>
    /// <param name="hex">A missing/null query field in an accept.</param>
    [Theory]
    [InlineData("830119801781182a")]
    [InlineData("830119801782182af6")]
    public void DecoderRejectsMissingOrNullQuery(string hex) =>
        _ = Assert.ThrowsAny<Exception>(() => CborSerializer.Deserialize<HandshakeMessage>(Convert.FromHexString(hex)));

    /// <summary>The concrete data codec cannot write a one-element array for a null query.</summary>
    [Fact]
    public void WriterRejectsNullQuery() =>
        _ = Assert.ThrowsAny<Exception>(() => CborSerializer.Serialize(new N2CVersionData(42, null)));

    /// <summary>Both handshake APIs check fixed arities, including child arrays, before interpreting a refusal.</summary>
    /// <param name="hex">A synthetic malformed fixed-arity handshake array.</param>
    /// <param name="generic">Whether the generic API is used.</param>
    [Theory]
    [InlineData("840119801782182af4656578747261", true)]
    [InlineData("840119801782182af4656578747261", false)]
    [InlineData("8202840219801763626164656578747261", true)]
    [InlineData("8202840219801763626164656578747261", false)]
    [InlineData("830119801783182af400", true)]
    [InlineData("830119801783182af400", false)]
    [InlineData("830282008119801700", true)]
    [InlineData("830282008119801700", false)]
    [InlineData("820283008119801700", true)]
    [InlineData("820283008119801700", false)]
    [InlineData("8303a000", true)]
    [InlineData("8303a000", false)]
    [InlineData("83010e85182af500f400", true)]
    [InlineData("83010e85182af500f400", false)]
    public async Task HandshakeRejectsTrailingElementsAsMalformedCbor(string hex, bool generic)
    {
        using ProtocolReplay replay = new(ProtocolType.Handshake, Convert.FromHexString(hex));
        Handshake handshake = new(replay.Channel);
        using CancellationTokenSource deadline = new(TimeSpan.FromSeconds(1));
        _ = await Assert.ThrowsAsync<FormatException>(async () =>
        {
            ProposeVersions proposal = HandshakeMessages.ProposeVersions(VersionTables.N2cV10AndAbove(42));
            if (generic)
            {
                _ = await handshake.SendAsync<HandshakeMessage>(proposal, deadline.Token);
            }
            else
            {
                _ = await handshake.SendAsync(proposal, deadline.Token);
            }
        });
        Assert.False(handshake.IsDone);
    }

    /// <summary>Arity validation also sees a header declaring extra elements when the frame arrived in pieces.</summary>
    [Fact]
    public async Task FragmentedAcceptWithExtraElementIsMalformed()
    {
        using ProtocolReplay replay = new(ProtocolType.Handshake, Convert.FromHexString("840119"), Convert.FromHexString("801782182af4656578747261"));
        Handshake handshake = new(replay.Channel);
        using CancellationTokenSource deadline = new(TimeSpan.FromSeconds(1));
        _ = await Assert.ThrowsAsync<FormatException>(() => handshake.SendAsync<N2CAcceptVersion>(HandshakeMessages.ProposeVersions(VersionTables.N2cV10AndAbove(42)), deadline.Token));
        Assert.False(handshake.IsDone);
    }

    /// <summary>Refusal versions are Word16 values, and a version mismatch must have a nonnull list.</summary>
    /// <param name="hex">An invalid refusal according to the pinned CDDL.</param>
    [Theory]
    [InlineData("820283022063626164")]
    [InlineData("820283012063626164")]
    [InlineData("820282008120")]
    [InlineData("820283021a0001000063626164")]
    [InlineData("820283011a0001000063626164")]
    [InlineData("82028200811a00010000")]
    [InlineData("82028100")]
    public void DecoderRejectsInvalidRefusalVersions(string hex) =>
        _ = Assert.ThrowsAny<Exception>(() => CborSerializer.Deserialize<HandshakeMessage>(Convert.FromHexString(hex)));

    /// <summary>Fresh refusal records enforce the same unsigned Word16 version domain on write.</summary>
    /// <param name="version">The invalid wire version.</param>
    [Theory]
    [InlineData(-1)]
    [InlineData(65536)]
    public void WriterRejectsInvalidRefusalVersions(int version)
    {
        RefuseReason[] reasons = [new VersionMismatch(0, CborDefList<int>.Create([version])), new HandshakeDecodeError(1, version, "bad"), new Refused(2, version, "bad")];
        Assert.All(reasons, reason => _ = Assert.ThrowsAny<Exception>(() => CborSerializer.Serialize(reason)));
    }

    /// <summary>Null mismatch lists fail validation rather than leaking an incidental null dereference from writing.</summary>
    [Fact]
    public void VersionMismatchValidatorRejectsMissingVersionList()
    {
        Type validatorType = typeof(HandshakeMessage).Assembly.GetType("Chrysalis.Network.Cbor.Handshake.VersionMismatchValidator", throwOnError: true)!;
        ICborValidator<VersionMismatch> validator = (ICborValidator<VersionMismatch>)Activator.CreateInstance(validatorType, nonPublic: true)!;
        Assert.False(validator.Validate(new VersionMismatch(0, default)));
    }
}
