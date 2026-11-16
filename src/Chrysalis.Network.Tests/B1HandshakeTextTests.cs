using System.Reflection;
using Chrysalis.Codec.Serialization;
using Chrysalis.Network.Cbor.Handshake;
using Xunit;

namespace Chrysalis.Network.Tests;

/// <summary>D26's text contract, including fresh encodes that cannot reuse a decoded Raw slice.</summary>
public sealed class B1HandshakeTextTests
{
    /// <summary>Both node text members are strings, not bytes hidden behind an additive accessor.</summary>
    /// <param name="refused">Selects explicit refusal rather than a version-data decode error.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ErrorDataIsStringWithoutErrorText(bool refused)
    {
        Type type = refused ? typeof(Refused) : typeof(HandshakeDecodeError);
        Assert.Equal(typeof(string), type.GetProperty("ErrorData")?.PropertyType);
        Assert.Null(type.GetProperty("ErrorText"));
    }

    /// <summary>The pinned handshake CDDL's tstr preserves non-ASCII UTF-8 without normalization.</summary>
    /// <param name="hex">Synthetic [2,[reason,32791,"héllo"]], from the pinned handshake CDDL.</param>
    /// <param name="refused">Selects the expected reason alternative.</param>
    [Theory]
    [InlineData("820283011980176668c3a96c6c6f", false)]
    [InlineData("820283021980176668c3a96c6c6f", true)]
    public void NodeTextDecodesExactly(string hex, bool refused)
    {
        byte[] frame = Convert.FromHexString(hex);
        Refuse reply = Assert.IsType<Refuse>(CborSerializer.Deserialize<HandshakeMessage>(frame, out int consumed));
        Assert.Equal(frame.Length, consumed);
        Assert.Equal(refused ? typeof(Refused) : typeof(HandshakeDecodeError), reply.Reason.GetType());
        Assert.Equal("héllo", Text(reply.Reason));
    }

    /// <summary>New records must emit CBOR text, independently of the Raw round-trip shortcut.</summary>
    /// <param name="hex">Synthetic UTF-8 text encoding according to the pinned CDDL.</param>
    /// <param name="refused">Selects the reason alternative to construct.</param>
    [Theory]
    [InlineData("820283011980176668c3a96c6c6f", false)]
    [InlineData("820283021980176668c3a96c6c6f", true)]
    public void FreshReasonEncodesUtf8Text(string hex, bool refused)
    {
        RefuseReason reason = FreshReason(refused, "héllo");
        Assert.True(reason.Raw.IsEmpty);
        Assert.Equal(Convert.FromHexString(hex), CborSerializer.Serialize(new Refuse(2, reason)));
        HandshakeRefusedException failure = new(reason);
        Assert.Same(reason, failure.Reason);
        Assert.Contains("héllo", failure.Message, StringComparison.Ordinal);
    }

    /// <summary>Byte strings and null are not text, even when a decoder can otherwise consume them.</summary>
    /// <param name="hex">Synthetic bstr/null variants of the two text-bearing reason shapes.</param>
    [Theory]
    [InlineData("820283011980174568656c6c6f")]
    [InlineData("820283021980174568656c6c6f")]
    [InlineData("82028301198017f6")]
    [InlineData("82028302198017f6")]
    public void RefusalRejectsNonTextPayload(string hex)
    {
        byte[] frame = Convert.FromHexString(hex);
        _ = Assert.ThrowsAny<Exception>(() => CborSerializer.Deserialize<HandshakeMessage>(frame));
    }

    /// <summary>Fresh encoding also rejects null instead of emitting an invalid reason.</summary>
    /// <param name="refused">Selects the text-bearing reason to construct.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void FreshReasonRejectsNullText(bool refused)
    {
        RefuseReason reason = FreshReason(refused, null);
        _ = Assert.ThrowsAny<Exception>(() => CborSerializer.Serialize(new Refuse(2, reason)));
    }

    private static string Text(RefuseReason reason) => Assert.IsType<string>(reason.GetType().GetProperty("ErrorData")?.GetValue(reason));

    private static RefuseReason FreshReason(bool refused, string? text, int version = 32791)
    {
        Type type = refused ? typeof(Refused) : typeof(HandshakeDecodeError);
        ConstructorInfo? constructor = type.GetConstructor([typeof(int), typeof(int), typeof(string)]);
        Assert.NotNull(constructor);
        return Assert.IsAssignableFrom<RefuseReason>(constructor.Invoke([refused ? 2 : 1, version, text]));
    }
}
