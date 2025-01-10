using System.Collections.Concurrent;
using System.Reflection;
using Chrysalis.Cbor.Attributes;
using Chrysalis.Cbor.Types;

public static class RuntimeMetadataCache
{
    private static readonly ConcurrentDictionary<Assembly, Type[]> _typeCache = new();
    private static readonly ConcurrentDictionary<Type, object[]> _attributeCache = new();

    // Get or load types from an assembly
    public static Type[] GetTypes(Assembly assembly)
    {
        return _typeCache.GetOrAdd(assembly, assembly =>
        {
            try
            {
                return assembly.GetTypes();
            }
            catch (ReflectionTypeLoadException ex)
            {
                return ex.Types.Where(t => t != null).Cast<Type>().ToArray();
            }
        });
    }

    // Get or load attributes for a type
    public static object[] GetAttributes(Type type)
    {
        return _attributeCache.GetOrAdd(type, t => t.GetCustomAttributes().ToArray());
    }
}
