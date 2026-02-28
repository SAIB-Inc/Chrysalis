using System.Runtime.CompilerServices;

namespace Chrysalis.Cbor.Serialization;

/// <summary>
/// Tracks indefinite encoding state for collection instances that don't inherit from CborBase.
/// Uses ConditionalWeakTable to avoid memory leaks and provide automatic cleanup.
/// </summary>
public static class IndefiniteStateTracker
{
    private static readonly ConditionalWeakTable<object, IndefiniteState> _states = [];

    internal sealed class IndefiniteState
    {
        /// <summary>
        /// Gets or sets a value indicating whether the collection uses indefinite encoding.
        /// </summary>
        public bool IsIndefinite { get; set; }
    }

    /// <summary>
    /// Marks a collection as having been read with indefinite encoding.
    /// </summary>
    /// <param name="collection">The collection instance to mark.</param>
    public static void SetIndefinite(object collection)
    {
        if (collection == null)
        {
            return;
        }

        _states.AddOrUpdate(collection, new IndefiniteState { IsIndefinite = true });
    }

    /// <summary>
    /// Checks if a collection was read with indefinite encoding.
    /// </summary>
    /// <param name="collection">The collection instance to check.</param>
    /// <returns>True if the collection was encoded with indefinite length; otherwise, false.</returns>
    public static bool IsIndefinite(object collection)
    {
        return collection != null && _states.TryGetValue(collection, out IndefiniteState? state) && state.IsIndefinite;
    }
}
