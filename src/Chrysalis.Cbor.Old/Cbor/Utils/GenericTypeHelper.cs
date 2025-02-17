namespace Chrysalis.Cbor.Utils;

internal static class GenericTypeHelper
{
    internal static bool IsGenericImplementationOf(Type potentialImpl, Type baseType)
    {
        if (!baseType.IsGenericTypeDefinition || !potentialImpl.IsGenericTypeDefinition)
            return false;

        return GetBaseTypes(potentialImpl)
            .Where(t => t.IsGenericType)
            .Select(t => t.GetGenericTypeDefinition())
            .Any(t => t == baseType);
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
}