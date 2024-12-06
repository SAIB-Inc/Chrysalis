using System.Reflection;

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