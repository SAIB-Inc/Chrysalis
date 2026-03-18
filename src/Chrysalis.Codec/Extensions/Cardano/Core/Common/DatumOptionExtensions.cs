using Chrysalis.Codec.Serialization;
using Chrysalis.Codec.Types;
using Chrysalis.Codec.Types.Cardano.Core.Common;

namespace Chrysalis.Codec.Extensions.Cardano.Core.Common;

/// <summary>
/// Extension methods for <see cref="IDatumOption"/> to access option tag and data.
/// </summary>
public static class DatumOptionExtensions
{
    /// <summary>
    /// Gets the option tag indicating the datum type (hash or inline).
    /// </summary>
    /// <param name="self">The datum option instance.</param>
    /// <returns>The option tag value.</returns>
    public static int Option(this IDatumOption self)
    {
        ArgumentNullException.ThrowIfNull(self);
        return self switch
        {
            DatumHashOption datumHashOption => datumHashOption.Tag,
            InlineDatumOption inlineDatumOption => inlineDatumOption.Tag,
            _ => throw new NotImplementedException()
        };
    }

    /// <summary>
    /// Gets the inline datum data bytes, or empty if the datum is a hash reference.
    /// </summary>
    /// <param name="self">The datum option instance.</param>
    /// <returns>The datum data bytes.</returns>
    public static ReadOnlyMemory<byte> Data(this IDatumOption self)
    {
        ArgumentNullException.ThrowIfNull(self);
        return self switch
        {
            InlineDatumOption inlineDatumOption => inlineDatumOption.Data.Value,
            _ => ReadOnlyMemory<byte>.Empty
        };
    }

    /// <summary>
    /// Creates an <see cref="InlineDatumOption"/> from a typed CBOR value.
    /// Handles the serialize → PlutusData roundtrip → tag-24 wrapping automatically.
    /// </summary>
    public static InlineDatumOption InlineDatumFrom<T>(T value) where T : ICborType
    {
        ArgumentNullException.ThrowIfNull(value);
        byte[] plutusBytes = CborSerializer.Serialize(
            CborSerializer.Deserialize<IPlutusData>(CborSerializer.Serialize(value)));
        return InlineDatumOption.Create(1, new CborEncodedValue(plutusBytes));
    }
}
