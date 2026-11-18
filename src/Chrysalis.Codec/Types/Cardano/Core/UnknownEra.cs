using System.Globalization;
using Chrysalis.Codec.Serialization;
using SAIB.Cbor;
using SAIB.Cbor.Serialization;

namespace Chrysalis.Codec.Types.Cardano.Core;

/// <summary>The wire envelope containing an era tag.</summary>
public enum EraEnvelope
{
    /// <summary>An era-tagged block.</summary>
    Block,
    /// <summary>A node-to-node chain-sync header.</summary>
    Header
}

/// <summary>
/// An explicitly unprocessed envelope. The tag is the wire number, not a ledger <see cref="Era"/>.
/// Point is caller-supplied context, or null: an unknown layout cannot safely yield a chain point.
/// The readers retain an owned copy of the envelope, safe after the input buffer is released.
/// </summary>
public readonly record struct UnknownEra(
    ulong WireTag,
    EraEnvelope Envelope,
    ReadOnlyMemory<byte> Raw,
    (ulong Slot, ReadOnlyMemory<byte> Hash)? Point = null) : ICborType
{
    private const string EnvelopeShapeError = "An era envelope must contain exactly a tag and a body.";

    /// <inheritdoc />
    public int ConstrIndex => 0;
    /// <inheritdoc />
    public bool IsIndefinite => !Raw.IsEmpty && Raw.Span[0] == 0x9f;

    /// <summary>Validates an envelope against its supported wire-tag bound without materializing its body.</summary>
    public static bool Validate<T>(T value, EraEnvelope envelope, ulong maximumTag) where T : ICborType
    {
        CborReader reader = new(value.Raw.Span);
        ulong tag = ReadTag(ref reader, out bool indefinite);
        if (tag <= maximumTag)
        {
            return true;
        }
        ReadOnlyMemory<byte> raw = ReadEnvelopeBody(value.Raw, ref reader, indefinite);
        throw new UnknownEraException(new UnknownEra(tag, envelope, raw.ToArray()));
    }

    /// <summary>Reads one unknown envelope verbatim. Known tags must use the normal typed reader.</summary>
    public static UnknownEra Read(ReadOnlyMemory<byte> data, EraEnvelope envelope, ulong maximumTag,
        (ulong Slot, ReadOnlyMemory<byte> Hash)? point = null)
    {
        CborReader reader = new(data.Span);
        ulong tag = ReadTag(ref reader, out bool indefinite);
        ReadOnlyMemory<byte> raw = ReadEnvelopeBody(data, ref reader, indefinite);
        if (tag <= maximumTag)
        {
            throw new ArgumentException("The era tag is supported; use the typed reader.", nameof(data));
        }
        return new UnknownEra(tag, envelope, raw.ToArray(), point);
    }

    /// <summary>Uses the normal reader, attaching a known caller point if the era is unsupported.</summary>
    public static T ReadAtPoint<T>(ReadOnlyMemory<byte> data,
        (ulong Slot, ReadOnlyMemory<byte> Hash) point) where T : ICborType
    {
        try
        {
            return CborSerializer.Deserialize<T>(data);
        }
        catch (UnknownEraException exception)
        {
            throw new UnknownEraException(exception.Value with { Point = point }, exception);
        }
    }

    private static ulong ReadTag(ref CborReader reader, out bool indefinite)
    {
        reader.ReadBeginArray();
        int size = reader.ReadSize();
        if (size is not (2 or -1))
        {
            throw new FormatException(EnvelopeShapeError);
        }
        indefinite = size == -1;
        return reader.ReadUInt64();
    }

    private static ReadOnlyMemory<byte> ReadEnvelopeBody(ReadOnlyMemory<byte> data, ref CborReader reader, bool indefinite)
    {
        if (reader.Buffer.IsEmpty || reader.Buffer[0] == 0xff)
        {
            throw new FormatException(EnvelopeShapeError);
        }
        reader.SkipDataItem();
        if (indefinite)
        {
            if (reader.Buffer.IsEmpty || reader.Buffer[0] != 0xff)
            {
                throw new FormatException(EnvelopeShapeError);
            }
            _ = reader.ReadDataItem();
        }
        return data[..(data.Length - reader.Buffer.Length)];
    }
}

/// <summary>An unsupported wire era, with the original envelope and any caller-supplied point.</summary>
public sealed class UnknownEraException : CborDecodeException
{
    /// <summary>Creates an unsupported-era error without wire context.</summary>
    public UnknownEraException() { }
    /// <summary>Creates an unsupported-era error with a custom message.</summary>
    public UnknownEraException(string message) : base(message) { }
    /// <summary>Creates an unsupported-era error with a custom message and cause.</summary>
    public UnknownEraException(string message, Exception innerException) : base(message, innerException) { }
    /// <summary>Creates an unsupported-era error retaining the original envelope.</summary>
    public UnknownEraException(UnknownEra value, Exception? innerException = null)
        : base(GetTagOffset(value.Raw), value.Envelope.ToString(), "supported wire era tag",
            value.WireTag.ToString(CultureInfo.InvariantCulture), innerException) => Value = value;
    private static int GetTagOffset(ReadOnlyMemory<byte> raw)
    {
        if (raw.IsEmpty) { return -1; }
        CborReader reader = new(raw.Span);
        try
        {
            reader.ReadBeginArray();
            _ = reader.ReadSize();
            return raw.Length - reader.Buffer.Length;
        }
        catch (CborException)
        {
            return -1;
        }
    }

    /// <summary>The unprocessed wire value; default only for manually constructed errors without wire context.</summary>
    public UnknownEra Value { get; }
    /// <summary>The tag exactly as numbered on the wire, without ledger-era conversion.</summary>
    public ulong WireTag => Value.WireTag;
    /// <summary>The envelope containing the tag.</summary>
    public EraEnvelope Envelope => Value.Envelope;
    /// <summary>The preserved original envelope bytes.</summary>
    public ReadOnlyMemory<byte> Raw => Value.Raw;
    /// <summary>The caller's chain point, or null when no point was supplied.</summary>
    public (ulong Slot, ReadOnlyMemory<byte> Hash)? Point => Value.Point;
}
