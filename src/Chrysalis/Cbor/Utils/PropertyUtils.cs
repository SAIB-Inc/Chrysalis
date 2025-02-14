using System.Collections.Concurrent;
using System.Reflection;
using Chrysalis.Cbor.Attributes;

namespace Chrysalis.Cbor.Utils;

internal static class PropertyUtils
{
    private static readonly ConcurrentDictionary<Type, PropertyInfo[]> PropertyCache = new();

    public static PropertyInfo[] GetCborProperties(Type type) =>
        PropertyCache.GetOrAdd(type, t => t.GetProperties()
            .Where(p => p.GetCustomAttribute<CborPropertyAttribute>() != null)
            .OrderBy(p => p.GetCustomAttribute<CborPropertyAttribute>()?.Index ?? int.MaxValue)
            .ToArray());
}