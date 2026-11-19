using Chrysalis.Codec.Types;

namespace Chrysalis.Tx.Extensions;

/// <summary>
/// Convenience extensions for wrapping lists as CBOR sets.
/// </summary>
public static class CborListExtensions
{
    /// <summary>
    /// Wraps a list as a <see cref="CborList{T}"/> (CBOR tagged definite-length set).
    /// </summary>
    public static CborList<T> ToCborSet<T>(this List<T> list)
        => CborList.Create(list, hasSetTag: true);
}
