using System.Runtime.CompilerServices;

namespace Chrysalis.Codec.Serialization;

public static class IndefiniteStateTracker
{
    private static readonly ConditionalWeakTable<object, IndefiniteState> _states = [];

    private sealed class IndefiniteState;
    private static readonly IndefiniteState _indefinite = new();

    public static void SetIndefinite(object collection)
    {
        if (collection is null)
        {
            return;
        }

        _ = _states.TryAdd(collection, _indefinite);
    }

    public static bool IsIndefinite(object collection) => collection is not null && _states.TryGetValue(collection, out _);
}
