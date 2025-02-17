using System.Collections.Concurrent;
using System.Reflection;

namespace Chrysalis.Cbor.Serialization.Registry;

/// <summary>
/// Registry for CBOR converters
/// </summary>
public sealed class CborConverterRegistry
{
    private readonly ConcurrentDictionary<Type, ICborConverter> _converters = new();

    public void Initialize(Assembly assembly)
    {
        IEnumerable<Type> converterTypes = assembly.GetTypes()
            .Where(t => !t.IsAbstract && !t.IsInterface)
            .Where(t => typeof(ICborConverter).IsAssignableFrom(t));

        foreach (Type? converterType in converterTypes)
        {
            if (Activator.CreateInstance(converterType) is ICborConverter converter)
                _converters.TryAdd(converterType, converter);

        }
    }

    public ICborConverter? GetConverter(Type converterType) =>
        _converters.GetValueOrDefault(converterType);
}