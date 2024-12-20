using System.Collections.Concurrent;
using System.Reflection;
using Chrysalis.Cbor.Attributes;

namespace Chrysalis.Cbor.Utils;

public static class AssemblyUtils
{
    private static readonly ConcurrentDictionary<Type, (Type[] BaseTypes, Type[] Interfaces)> _typeHierarchyCache = new();

    public static Type[] FindConcreteTypes(Type baseType, IEnumerable<Assembly> assemblies)
    {
        Type baseTypeDefinition = baseType.IsGenericType
            ? baseType.GetGenericTypeDefinition()
            : baseType;

        return assemblies
            .SelectMany(RuntimeMetadataCache.GetTypes)
            .Where(type =>
                type != null &&
                !type.IsInterface &&
                !type.IsAbstract &&
                IsAssignableToGenericType(type, baseTypeDefinition))
            .ToArray();
    }


    public static IEnumerable<(int? Index, string Name, Type Type)> GetCborPropertiesOrParameters(Type type)
    {
        // First try constructor parameters
        ConstructorInfo? constructor = type.GetConstructors().FirstOrDefault();
        if (constructor != null)
        {
            return constructor.GetParameters()
                .Select(p =>
                {
                    // Get the CborProperty attribute directly from the parameter
                    CborPropertyAttribute? attr = p.GetCustomAttribute<CborPropertyAttribute>();
                    return (
                        attr?.Index,
                        Name: attr?.PropertyName ?? p.Name ?? "",
                        Type: p.ParameterType
                    );
                })
                .Where(p => p.Name != "Raw");
        }

        // Fallback to properties
        return type.GetProperties()
            .Where(p =>
                p.CanRead &&
                p.CanWrite &&
                p.Name != "Raw" &&
                p.GetCustomAttribute<CborPropertyAttribute>() != null)
            .Select(p =>
            {
                CborPropertyAttribute? attr = p.GetCustomAttribute<CborPropertyAttribute>();
                return (
                    attr?.Index,
                    Name: attr?.PropertyName ?? p.Name,
                    Type: p.PropertyType
                );
            });
    }

    private static bool IsAssignableToGenericType(Type givenType, Type genericType)
    {
        if (!genericType.IsGenericTypeDefinition)
        {
            return genericType.IsAssignableFrom(givenType);
        }

        // Cache the type hierarchy and interfaces
        var typeHierarchy = _typeHierarchyCache.GetOrAdd(givenType, t =>
        {
            var baseTypes = new List<Type>();
            var currentType = t;
            while (currentType != null && currentType != typeof(object))
            {
                baseTypes.Add(currentType);
                currentType = currentType.BaseType;
            }

            var interfaces = t.GetInterfaces();
            return (baseTypes.ToArray(), interfaces);
        });

        // Check base types
        if (typeHierarchy.BaseTypes.Any(t => t.IsGenericType && t.GetGenericTypeDefinition() == genericType))
            return true;

        // Check interfaces
        return typeHierarchy.Interfaces.Any(i => i.IsGenericType && i.GetGenericTypeDefinition() == genericType);
    }
}