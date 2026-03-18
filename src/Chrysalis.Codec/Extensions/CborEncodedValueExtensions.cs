using Chrysalis.Codec.Serialization;
using Chrysalis.Codec.Types;

namespace Chrysalis.Codec.Extensions;

/// <summary>
/// Extension methods for <see cref="CborEncodedValue"/>.
/// </summary>
public static class CborEncodedValueExtensions
{
    /// <summary>
    /// Returns the inner CBOR bytes from a CborEncodedValue.
    /// Since <see cref="CborEncodedValue.Value"/> now stores the inner bytes
    /// directly (tag 24 + bytestring wrapping is handled by the codegen),
    /// this simply returns <see cref="CborEncodedValue.Value"/> as a byte[].
    /// </summary>
    public static byte[] GetValue(this CborEncodedValue self)
    {
        ArgumentNullException.ThrowIfNull(self);
        return self.Value.ToArray();
    }

    /// <summary>
    /// Deserializes the inner CBOR bytes as <typeparamref name="T"/>.
    /// </summary>
    public static T Deserialize<T>(this CborEncodedValue self) where T : ICborType
    {
        ArgumentNullException.ThrowIfNull(self);
        return CborSerializer.Deserialize<T>(self.Value);
    }
}
