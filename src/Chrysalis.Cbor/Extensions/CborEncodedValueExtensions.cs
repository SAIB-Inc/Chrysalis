using Chrysalis.Cbor.Serialization;
using Chrysalis.Cbor.Types;
using Dahomey.Cbor.Serialization;

namespace Chrysalis.Cbor.Extensions;

/// <summary>
/// Extension methods for <see cref="CborEncodedValue"/>.
/// </summary>
public static class CborEncodedValueExtensions
{
    /// <summary>
    /// Extracts the inner byte string from a CBOR encoded value by reading past the tag.
    /// Allocates a new byte[] for the result.
    /// </summary>
    public static byte[] GetValue(this CborEncodedValue self)
    {
        ArgumentNullException.ThrowIfNull(self);
        CborReader reader = new(self.Value.Span);
        _ = reader.TryReadSemanticTag(out _);
        return reader.ReadByteString().ToArray();
    }

    /// <summary>
    /// Unwraps the CBOR tag 24 envelope and deserializes the inner byte string directly
    /// as <typeparamref name="T"/> without allocating an intermediate byte[].
    /// </summary>
    public static T Deserialize<T>(this CborEncodedValue self) where T : CborBase
    {
        ArgumentNullException.ThrowIfNull(self);
        ReadOnlySpan<byte> span = self.Value.Span;
        int tagHeaderSize = CborHeaderSize(span);
        int bstrHeaderSize = CborHeaderSize(span[tagHeaderSize..]);
        return CborSerializer.Deserialize<T>(self.Value[(tagHeaderSize + bstrHeaderSize)..]);
    }

    /// <summary>
    /// Computes the CBOR initial byte + additional info header size.
    /// Works for any major type (tags, byte strings, etc.).
    /// </summary>
    private static int CborHeaderSize(ReadOnlySpan<byte> data)
    {
        int additionalInfo = data[0] & 0x1F;
        return additionalInfo switch
        {
            < 24 => 1,
            24 => 2,
            25 => 3,
            26 => 5,
            27 => 9,
            _ => throw new FormatException($"Unexpected CBOR additional info: {additionalInfo}")
        };
    }
}
