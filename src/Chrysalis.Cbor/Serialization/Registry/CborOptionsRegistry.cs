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

    public CborOptions GetOptions(Type type) =>
        _optionsCache.GetValueOrDefault(type.NormalizeType()) ?? CborOptions.Default;

    private static CborOptions BuildOptionsForType(Type type)
    {
        Type normalizedType = type.NormalizeType();
        CborOptionsAttribute? optionsAttr = AttributeResolver.GetInheritedAttribute<CborOptionsAttribute>(type);
        Type? converterType = AttributeResolver.ResolveConverterType(type);
        IReadOnlyCollection<Type>? unionTypes = UnionResolver.ResolveUnionTypes(type, converterType);
        (IReadOnlyDictionary<int, Type> indexMap, IReadOnlyDictionary<string, Type> namedMap, ConstructorInfo constructor) = PropertyResolver.ResolvePropertyMappings(type);

        return new CborOptions(
            index: optionsAttr?.Index ?? -1,
            isDefinite: optionsAttr?.IsDefinite ?? false,
            tag: optionsAttr?.Tag ?? -1,
            size: optionsAttr?.Size ?? -1,
            objectType: type,
            normalizedType: normalizedType,
            converterType: converterType,
            indexPropertyMapping: indexMap,
            namedPropertyMapping: namedMap,
            unionTypes: unionTypes,
            constructor: constructor
        );
    }
}