using System.Formats.Cbor;
using Chrysalis.Cbor.Serialization;
using Chrysalis.Cbor.Types;

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
    /// <param name="self">The CBOR encoded value.</param>
    /// <returns>The inner byte string value.</returns>
    public static byte[] GetValue(this CborEncodedValue self)
    {
        ArgumentNullException.ThrowIfNull(self);
        CborReader reader = new(self.Value, CborConformanceMode.Lax);
        _ = reader.ReadTag();
        return reader.ReadByteString();
    }

    /// <summary>
    /// Unwraps the CBOR tag 24 envelope and deserializes the inner byte string directly
    /// as <typeparamref name="T"/> without allocating an intermediate byte[].
    /// </summary>
    /// <typeparam name="T">The CBOR type to deserialize the inner value as.</typeparam>
    /// <param name="self">The CBOR encoded value.</param>
    /// <returns>The deserialized object.</returns>
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
