using System.Collections.Concurrent;
using System.Reflection;
using Chrysalis.Cbor.Converters;
using Chrysalis.Cbor.Types;

namespace Chrysalis.Cbor.CacheRegistry;

internal class RegistryStorage
{
    public ConcurrentDictionary<Type, ICborConverter> Converters { get; } = new();
    public ConcurrentDictionary<Type, CborOptions> Options { get; } = new();
    public ConcurrentDictionary<Type, Delegate> Activators { get; } = new();
    public ConcurrentDictionary<Type, PropertyInfo[]> Properties { get; } = new();
    public ConcurrentDictionary<(Type Type, string Name), Delegate> Getters { get; } = new();
    public ConcurrentDictionary<(Type Type, string Name), Delegate> Setters { get; } = new();

    public void Clear()
    {
        Converters.Clear();
        Options.Clear();
        Activators.Clear();
        Properties.Clear();
        Getters.Clear();
        Setters.Clear();
    }
}