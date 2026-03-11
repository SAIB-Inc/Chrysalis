using System.Runtime.CompilerServices;

namespace Chrysalis.Codec.Serialization;

public static class IndefiniteStateTracker
{
    private static readonly ConditionalWeakTable<object, IndefiniteState> _states = [];

    internal sealed class IndefiniteState
    {
        public bool IsIndefinite { get; set; }
    }

    public static void SetIndefinite(object collection)
    {
        if (collection == null)
        {
            return;
        }

        _states.AddOrUpdate(collection, new IndefiniteState { IsIndefinite = true });
    }

    public static bool IsIndefinite(object collection) => collection != null && _states.TryGetValue(collection, out IndefiniteState? state) && state.IsIndefinite;
}
