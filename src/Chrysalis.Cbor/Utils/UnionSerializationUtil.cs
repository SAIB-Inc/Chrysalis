using Chrysalis.Cbor.Serialization;

namespace Chrysalis.Cbor.Utils;

public static class UnionSerializationUtil
{
    public static IEnumerable<Type> ResolveConcreteTypes(CborOptions options)
    {
        if (!options.RuntimeType!.IsGenericType)
            return options.UnionTypes!;

        Type[] typeArgs = options.RuntimeType.GetGenericArguments();
        return [.. options.UnionTypes!.Select(t => t.IsGenericTypeDefinition ? t.MakeGenericType(typeArgs) : t)];
    }

    public static void ThrowDeserializationError(Dictionary<Type, Exception> errors, byte[] data)
    {
        string details = string.Join(Environment.NewLine,
            errors.Select(kvp => $"Type {kvp.Key.Name}: {kvp.Value.Message}"));

        throw new InvalidOperationException(
            $"Failed to deserialize union type. Errors:{Environment.NewLine}{details} Cbor data: {Convert.ToHexString(data)}");
    }
}