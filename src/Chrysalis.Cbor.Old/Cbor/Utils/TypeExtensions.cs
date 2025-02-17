namespace Chrysalis.Cbor.Utils;

internal static class TypeExtensions
{
    internal static IEnumerable<Type> GetBaseTypes(this Type type)
    {
        var current = type;
        while (current != null && current != typeof(object))
        {
            yield return current;
            current = current.BaseType;
        }
    }
}