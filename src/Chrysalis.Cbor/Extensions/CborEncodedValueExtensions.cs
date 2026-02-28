using System.Formats.Cbor;
using Chrysalis.Cbor.Types;

namespace Chrysalis.Cbor.Extensions;

/// <summary>
/// Extension methods for <see cref="CborEncodedValue"/>.
/// </summary>
public static class CborEncodedValueExtensions
{
    /// <summary>
    /// Extracts the inner byte string from a CBOR encoded value by reading past the tag.
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
}
