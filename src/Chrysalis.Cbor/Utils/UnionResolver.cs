using System.Runtime.CompilerServices;
using Chrysalis.Cbor.Serialization.Converters.Custom;

namespace Chrysalis.Cbor.Utils;

public static class UnionResolver
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static IReadOnlyCollection<Type>? ResolveUnionTypes(Type type, Type? converterType)
    {
        if (converterType != typeof(UnionConverter))
            return null;

        if (!type.IsGenericType)
        {
            return [.. type.Assembly.GetTypes()
                .Where(t => t != type &&
                    !t.IsAbstract &&
                    type.IsAssignableFrom(t)
                )
            ];
        }

        // Handle open generic types
        Type genericTypeDef = type.GetGenericTypeDefinition();
        return [.. type.Assembly.GetTypes()
            .Where(t => t.IsGenericType &&
                       !t.IsAbstract &&
                       t != type &&
                       IsGenericSubclassOf(t.GetGenericTypeDefinition(), genericTypeDef
                    )
                )
            ];
    }

    private static bool IsGenericSubclassOf(Type generic, Type baseType)
    {
        while (generic != null && generic != typeof(object))
        {
            Type cur = generic.IsGenericType ? generic.GetGenericTypeDefinition() : generic;
            if (baseType == cur) return true;
            generic = generic.BaseType!;
        }
        return false;
    }
}