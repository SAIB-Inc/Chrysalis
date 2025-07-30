using System.Runtime.CompilerServices;

namespace Chrysalis.Cbor.Serialization;

/// <summary>
/// Tracks indefinite encoding state for collection instances that don't inherit from CborBase.
/// Uses ConditionalWeakTable to avoid memory leaks and provide automatic cleanup.
/// </summary>
public static class IndefiniteStateTracker
{
    private static readonly ConditionalWeakTable<object, IndefiniteState> _states = new();
    
    internal class IndefiniteState
    {
        public bool IsIndefinite { get; set; }
    }
    
    /// <summary>
    /// Marks a collection as having been read with indefinite encoding.
    /// </summary>
    public static void SetIndefinite(object collection)
    {
        if (collection == null) return;
        _states.AddOrUpdate(collection, new IndefiniteState { IsIndefinite = true });
    }
    
    /// <summary>
    /// Checks if a collection was read with indefinite encoding.
    /// </summary>
    public static bool IsIndefinite(object collection)
    {
        if (collection == null) return false;
        return _states.TryGetValue(collection, out var state) && state.IsIndefinite;
    }
}