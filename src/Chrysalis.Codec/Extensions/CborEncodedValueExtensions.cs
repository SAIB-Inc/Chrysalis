using Chrysalis.Codec.Serialization;
using Chrysalis.Codec.Types;

namespace Chrysalis.Codec.Extensions;

/// <summary>
/// Extension methods for <see cref="CborEncodedValue"/>.
/// </summary>
public static class CborEncodedValueExtensions
{
    /// <summary>
    /// Returns an independent copy of the inner CBOR bytes, without the tag or byte string headers.
    /// </summary>
    public static byte[] GetValue(this CborEncodedValue self) => self.Value.ToArray();

    /// <summary>
    /// Deserializes the inner CBOR bytes as <typeparamref name="T"/>.
    /// </summary>
    public static T Deserialize<T>(this CborEncodedValue self) where T : ICborType => CborSerializer.Deserialize<T>(self.Value);
}
