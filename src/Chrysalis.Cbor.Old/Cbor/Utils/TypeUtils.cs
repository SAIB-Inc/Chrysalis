using System.Collections.Concurrent;
using System.Reflection;
using Chrysalis.Cbor.Attributes;
using Chrysalis.Cbor.Converters;
using Chrysalis.Cbor.Converters.Primitives;
using Chrysalis.Cbor.Types;

namespace Chrysalis.Cbor.Utils;

internal static class TypeUtils
{
    private static readonly ConcurrentDictionary<Type, bool> CborTypeCache = new();

    public static Dictionary<Type, List<Type>> FindUnionTypes(IEnumerable<Type> types)
    {
        var unionTypes = types.Where(t =>
            t.GetCustomAttribute<CborConverterAttribute>()?.ConverterType == typeof(UnionConverter));

        return unionTypes.ToDictionary(
            t => t,
            t => types.Where(impl => !impl.IsAbstract && t.IsAssignableFrom(impl)).ToList());
    }

    public static IEnumerable<Type> DiscoverCborTypes()
    {
        var types = new HashSet<Type>();
        var assemblies = AppDomain.CurrentDomain.GetAssemblies();

        foreach (var assembly in assemblies)
        {
            foreach (var type in assembly.GetTypes())
            {
                // If this type inherits from CborBase
                if (typeof(CborBase).IsAssignableFrom(type))
                {
                    // Add the type itself
                    types.Add(type);

                    // Add all its base types up to CborBase
                    var currentType = type.BaseType;
                    while (currentType != null && currentType != typeof(object))
                    {
                        if (typeof(CborBase).IsAssignableFrom(currentType))
                        {
                            types.Add(currentType);
                        }
                        currentType = currentType.BaseType;
                    }
                }
            }
        }

        return types;
    }

    public static bool IsCborType(Type type)
    {
        return typeof(CborBase).IsAssignableFrom(type);
    }

    private static IEnumerable<Type> GetTypeWithDependencies(Type type)
    {
        yield return type;

        if (type.IsGenericType)
        {
            yield return type.GetGenericTypeDefinition();
        }

        foreach (var ctor in type.GetConstructors())
        {
            foreach (var param in ctor.GetParameters())
            {
                if (IsCborType(param.ParameterType))
                {
                    foreach (var depType in GetTypeWithDependencies(param.ParameterType))
                    {
                        yield return depType;
                    }
                }
            }
        }

        foreach (var prop in type.GetProperties())
        {
            if (IsCborType(prop.PropertyType))
            {
                foreach (var depType in GetTypeWithDependencies(prop.PropertyType))
                {
                    yield return depType;
                }
            }
        }
    }

    private static IEnumerable<Type> GetBaseTypes(Type type)
    {
        var current = type;
        while (current != null && current != typeof(object))
        {
            yield return current;
            current = current.BaseType;
        }
    }

    public static IEnumerable<Type> DiscoverConverters()
    {
        // Get only the concrete converter implementations from the executing assembly
        return Assembly.GetExecutingAssembly().GetTypes()
            .Where(t => t.IsClass &&
                       !t.IsAbstract &&
                       typeof(ICborConverter).IsAssignableFrom(t));
    }
}