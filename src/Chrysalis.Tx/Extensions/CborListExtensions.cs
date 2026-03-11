using Chrysalis.Codec.Types;

namespace Chrysalis.Tx.Extensions;

/// <summary>
/// Convenience extensions for wrapping lists as CBOR sets.
/// </summary>
public static class CborListExtensions
{
    /// <summary>
    /// Wraps a list as a <see cref="CborDefListWithTag{T}"/> (CBOR tagged definite-length set).
    /// </summary>
    public static CborDefListWithTag<T> ToCborSet<T>(this List<T> list)
        => CborDefListWithTag<T>.Create(list);
}
