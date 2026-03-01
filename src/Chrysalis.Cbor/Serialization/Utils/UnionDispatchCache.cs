using System.Collections.Concurrent;
using Chrysalis.Cbor.Types;

namespace Chrysalis.Cbor.Serialization.Utils;

/// <summary>
/// Provides a per-union runtime dispatch cache keyed by integer discriminant values.
/// </summary>
public static class UnionDispatchCache
{
    /// <summary>
    /// Tries to get a cached dispatch delegate for a union/discriminant pair.
    /// </summary>
    /// <typeparam name="TUnion">The union base type.</typeparam>
    /// <param name="discriminant">The integer discriminant value.</param>
    /// <param name="dispatch">The cached dispatch delegate when present.</param>
    /// <returns><c>true</c> if a cached delegate was found; otherwise <c>false</c>.</returns>
    public static bool TryGet<TUnion>(int discriminant, out Func<ReadOnlyMemory<byte>, TUnion>? dispatch)
        where TUnion : CborBase
    {
        return DispatchByDiscriminant<TUnion>.Cache.TryGetValue(discriminant, out dispatch);
    }

    /// <summary>
    /// Adds a cached dispatch delegate for a union/discriminant pair.
    /// </summary>
    /// <typeparam name="TUnion">The union base type.</typeparam>
    /// <param name="discriminant">The integer discriminant value.</param>
    /// <param name="dispatch">The dispatch delegate to cache.</param>
    public static void Set<TUnion>(int discriminant, Func<ReadOnlyMemory<byte>, TUnion> dispatch)
        where TUnion : CborBase
    {
        ArgumentNullException.ThrowIfNull(dispatch);
        _ = DispatchByDiscriminant<TUnion>.Cache.TryAdd(discriminant, dispatch);
    }

    /// <summary>
    /// Removes a cached dispatch delegate for a union/discriminant pair.
    /// </summary>
    /// <typeparam name="TUnion">The union base type.</typeparam>
    /// <param name="discriminant">The integer discriminant value.</param>
    public static void Remove<TUnion>(int discriminant)
        where TUnion : CborBase
    {
        _ = DispatchByDiscriminant<TUnion>.Cache.TryRemove(discriminant, out _);
    }

    private static class DispatchByDiscriminant<TUnion>
        where TUnion : CborBase
    {
        internal static readonly ConcurrentDictionary<int, Func<ReadOnlyMemory<byte>, TUnion>> Cache = [];
    }
}
