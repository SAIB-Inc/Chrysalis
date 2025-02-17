using System.Reflection;
using System.Runtime.CompilerServices;
using Chrysalis.Cbor.Attributes;

namespace Chrysalis.Cbor.Utils;

public static class AttributeResolver
{
    public static T? GetInheritedAttribute<T>(Type type) where T : Attribute
    {
        for (Type? currentType = type; currentType is not null && currentType != typeof(object); currentType = currentType.BaseType)
        {
            T? attr = currentType.GetCustomAttribute<T>(false);
            if (attr != null) return attr;
        }
        return null;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Type? ResolveConverterType(Type type) =>
        GetInheritedAttribute<CborConverterAttribute>(type)?.ConverterType;
}