using System.Collections.Concurrent;
using System.Reflection;
using Chrysalis.Cbor.Attributes;
using Chrysalis.Cbor.Extensions;
using Chrysalis.Cbor.Types;
using Chrysalis.Cbor.Utils;

namespace Chrysalis.Cbor.Serialization.Registry;

/// <summary>
/// Registry for CBOR options
/// </summary>
public sealed class CborOptionsRegistry
{
    private readonly ConcurrentDictionary<Type, CborOptions> _optionsCache = new();

    public void Initialize(Assembly assembly)
    {
        IEnumerable<Type> types = assembly.GetTypes()
            .Where(t => typeof(CborBase).IsAssignableFrom(t));

        foreach (Type? type in types)
        {
            CborOptions options = BuildOptionsForType(type);
            _optionsCache.TryAdd(type.NormalizeType(), options);
        }
    }

    public CborOptions GetOptions(Type type)
    {

        CborOptions? options;
        if (_optionsCache.TryGetValue(type.NormalizeType(), out CborOptions? value))
        {
            options = value;
        }
        else
        {
            CborOptions newOptions = BuildOptionsForType(type);
            _optionsCache.TryAdd(type.NormalizeType(), newOptions);
            options = newOptions;
        }

        return options;
    }

    private static CborOptions BuildOptionsForType(Type type)
    {
        Type normalizedType = type.NormalizeType();
        CborOptionsAttribute? optionsAttr = type.GetCustomAttribute<CborOptionsAttribute>();
        CborConverterAttribute? converterTypeAttr = type.GetCustomAttribute<CborConverterAttribute>();
        IReadOnlyCollection<Type>? unionTypes = UnionResolver.ResolveUnionTypes(type, converterTypeAttr?.ConverterType);
        (IReadOnlyDictionary<int, (Type Type, object? ExpectedValue)> IndexMap, IReadOnlyDictionary<string, (Type Type, object? ExpectedValue)> NamedMap, ConstructorInfo Constructor) = PropertyResolver.ResolvePropertyMappings(type);

        return new CborOptions(
            index: optionsAttr?.Index ?? -1,
            isDefinite: optionsAttr?.IsDefinite ?? false,
            tag: optionsAttr?.Tag ?? -1,
            size: optionsAttr?.Size ?? -1,
            objectType: type,
            normalizedType: normalizedType,
            converterType: converterTypeAttr?.ConverterType,
            indexPropertyMapping: IndexMap,
            namedPropertyMapping: NamedMap,
            unionTypes: unionTypes,
            constructor: Constructor
        );
    }
}