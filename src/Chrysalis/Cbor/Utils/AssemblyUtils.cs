using System.Reflection;
using Chrysalis.Cbor.Attributes;

namespace Chrysalis.Cbor.Utils;

public static class AssemblyUtils
{
    public static Type[] FindConcreteTypes(Type baseType, IEnumerable<Assembly> assemblies)
    {
        Type baseTypeDefinition = baseType.IsGenericType
            ? baseType.GetGenericTypeDefinition()
            : baseType;

        return assemblies
            .Where(a => !a.IsDynamic)
            .SelectMany(a =>
            {
                try { return a.GetTypes(); }
                catch (ReflectionTypeLoadException ex)
                {
                    return ex.Types.Where(t => t != null).Cast<Type>();
                }
            })
            .Where(t => t != null &&
                       !t.IsInterface &&
                       !t.IsAbstract &&
                       IsAssignableToGenericType(t, baseTypeDefinition))
            .Cast<Type>()
            .ToArray();
    }

    public static IEnumerable<(int? Index, string Name, Type Type)> GetCborPropertiesOrParameters(Type type)
    {
        // First try to get constructor parameters
        var constructor = type.GetConstructors().FirstOrDefault();
        if (constructor != null)
        {
            var parameters = constructor.GetParameters()
                .Select(p =>
                {
                    var attr = p.GetCustomAttribute<CborPropertyAttribute>();
                    return (attr?.Index, Name: p.Name ?? "", Type: p.ParameterType);
                });

            // Filter out Raw property using named field
            return parameters.Where(p => p.Name != "Raw");
        }

        // Fallback to properties if no constructor
        return type.GetProperties()
            .Where(p =>
                p.CanRead &&
                p.CanWrite &&
                p.Name != "Raw" &&
                p.GetCustomAttribute<CborPropertyAttribute>() != null)
            .Select(p =>
            {
                var attr = p.GetCustomAttribute<CborPropertyAttribute>();
                return (attr?.Index, p.Name, Type: p.PropertyType);
            });
    }

    private static bool IsAssignableToGenericType(Type givenType, Type genericType)
    {
        // For non-generic types, use standard assignability
        if (!genericType.IsGenericTypeDefinition)
        {
            return genericType.IsAssignableFrom(givenType);
        }

        // Get the generic type definition of the given type if it's generic
        Type givenTypeDefinition = givenType.IsGenericType
            ? givenType.GetGenericTypeDefinition()
            : givenType;

        // Check base types
        Type? type = givenTypeDefinition;
        while (type != null && type != typeof(object))
        {
            if (type.IsGenericType && type.GetGenericTypeDefinition() == genericType)
                return true;
            type = type.BaseType;
        }

        // Check interfaces
        return givenTypeDefinition.GetInterfaces()
            .Where(it => it.IsGenericType)
            .Select(it => it.GetGenericTypeDefinition())
            .Any(it => it == genericType);
    }
}