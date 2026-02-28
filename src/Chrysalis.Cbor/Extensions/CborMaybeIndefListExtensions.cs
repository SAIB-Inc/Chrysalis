using Chrysalis.Cbor.Types;

namespace Chrysalis.Cbor.Extensions;

/// <summary>
/// Extension methods for <see cref="CborMaybeIndefList{T}"/> to extract values regardless of encoding.
/// </summary>
public static class CborMaybeIndefListExtensions
{
    /// <summary>
    /// Gets the values from a CBOR list regardless of whether it uses definite or indefinite encoding.
    /// </summary>
    /// <typeparam name="T">The element type of the list.</typeparam>
    /// <param name="self">The CBOR list instance.</param>
    /// <returns>An enumerable of the list values.</returns>
    public static IEnumerable<T> GetValue<T>(this CborMaybeIndefList<T> self)
    {
        ArgumentNullException.ThrowIfNull(self);
        return self switch
        {
            CborDefList<T> defList => defList.Value,
            CborIndefList<T> indefList => indefList.Value,
            CborDefListWithTag<T> defListWithTag => defListWithTag.Value,
            CborIndefListWithTag<T> indefListWithTag => indefListWithTag.Value,
            _ => []
        };
    }
}
