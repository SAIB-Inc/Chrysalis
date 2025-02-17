using System.Formats.Cbor;
using Chrysalis.Cbor.Serialization;
using Chrysalis.Cbor.Serialization.Registry;

namespace Chrysalis.Cbor.Utils;

public static class UnionSerializationUtil
{
    public static object? Read(ReadOnlyMemory<byte> data, CborOptions options)
    {
        if (options.UnionTypes is null || options.UnionTypes.Count == 0)
            throw new InvalidOperationException("Union types are not defined in options.");

        IEnumerable<Type> concreteTypes = ResolveConcreteTypes(options);
        Dictionary<Type, Exception> errors = [];

        foreach (Type type in concreteTypes)
        {
            try
            {
                CborReader reader = new(data, CborConformanceMode.Lax);
                CborOptions typeOptions = CborRegistry.Instance.GetOptions(type);
                typeOptions = typeOptions with { RuntimeType = type };
                object? value = CborSerializer.Deserialize(reader, typeOptions);

                typeOptions.RuntimeType = type;
                return value;
            }
            catch (Exception ex)
            {
                errors[type] = ex;
            }
        }

        ThrowDeserializationError(errors, data.ToArray());
        return null; // Never reached, just for compiler
    }

    private static IEnumerable<Type> ResolveConcreteTypes(CborOptions options)
    {
        if (!options.RuntimeType!.IsGenericType)
            return options.UnionTypes!;

        Type[] typeArgs = options.RuntimeType.GetGenericArguments();
        return [.. options.UnionTypes!.Select(t => t.IsGenericTypeDefinition ? t.MakeGenericType(typeArgs) : t)];
    }

    private static void ThrowDeserializationError(Dictionary<Type, Exception> errors, byte[] data)
    {
        string details = string.Join(Environment.NewLine,
            errors.Select(kvp => $"Type {kvp.Key.Name}: {kvp.Value.Message}"));

        throw new InvalidOperationException(
            $"Failed to deserialize union type. Errors:{Environment.NewLine}{details} Cbor data: {Convert.ToHexString(data)}");
    }
}