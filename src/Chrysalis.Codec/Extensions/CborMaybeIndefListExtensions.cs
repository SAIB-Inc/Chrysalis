using Chrysalis.Codec.Serialization;
using Chrysalis.Codec.Types;

namespace Chrysalis.Codec.Extensions;

/// <summary>
/// Extension methods for <see cref="ICborMaybeIndefList{T}"/> to extract values regardless of encoding.
/// </summary>
public static class CborMaybeIndefListExtensions
{
    /// <summary>
    /// Gets the values from a CBOR list regardless of whether it uses definite or indefinite encoding.
    /// Iterates lazily from the raw CBOR bytes without building an intermediate List.
    /// </summary>
    public static CborListEnumerable<T> GetValue<T>(this ICborMaybeIndefList<T> self)
    {
        ArgumentNullException.ThrowIfNull(self);
        return new CborListEnumerable<T>(self.Raw);
    }
}
