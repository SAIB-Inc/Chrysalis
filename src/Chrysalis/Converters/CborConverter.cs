using System.Reflection;
using Chrysalis.Attributes;
using Chrysalis.Types.Core;

namespace Chrysalis.Converters;

public class CborConverter : ICborConverter<ICbor>
{

    public ReadOnlyMemory<byte> Serialize(ICbor value)
    {
        // Find the first type in the inheritance chain with a CborSerializableAttribute
        Type? currentType = value.GetType();
        while (currentType is not null && currentType != typeof(object))
        {
            CborSerializableAttribute? attribute = currentType.GetCustomAttribute<CborSerializableAttribute>();
            if (attribute != null && attribute.Converter != null)
            {
                // Create converter instance
                object? converterInstance = Activator.CreateInstance(attribute.Converter);

                // Find the Serialize method
                MethodInfo serializeMethod = attribute.Converter.GetMethod("Serialize",
                    BindingFlags.Public | BindingFlags.Instance,
                    null,
                    [currentType],
                    null) ?? throw new Exception($"No Serialize method found for converter {attribute.Converter.Name}");

                // Invoke Serialize method with null check
                object? result = serializeMethod.Invoke(converterInstance, [value]);
                return result is not null
                    ? (ReadOnlyMemory<byte>)result
                    : throw new InvalidOperationException($"Serialization returned null for {value.GetType().Name}");
            }

            currentType = currentType.BaseType;
        }

        throw new Exception($"No serializable type found in the inheritance hierarchy for {value.GetType().Name}");
    }

    // Implement the interface method
    public static T Deserialize<T>(ReadOnlyMemory<byte> data) where T : ICbor
    {
        ICbor result = DeserializeWithType(data, typeof(T));
        return (T)result;
    }

    // Add the method that takes a Type parameter
    private static ICbor DeserializeWithType(ReadOnlyMemory<byte> data, Type targetType)
    {
        var types = Assembly.GetExecutingAssembly()
            .GetTypes()
            .Where(t => !t.IsInterface && !t.IsAbstract && targetType.IsAssignableFrom(t))
            .ToList();

        foreach (Type type in types)
        {
            var result = TryDeserialize(type, data);
            if (result != null)
            {
                return result;
            }
        }

        throw new InvalidOperationException(
            $"Could not deserialize data to any known type implementing {targetType.Name}. Attempted types: {string.Join(", ", types.Select(t => t.Name))}");
    }

    private static ICbor? TryDeserialize(Type type, ReadOnlyMemory<byte> data)
    {
        try
        {
            // Walk up the inheritance chain to find the converter
            Type? currentType = type;
            while (currentType is not null && currentType != typeof(object))
            {
                CborSerializableAttribute? attribute = currentType.GetCustomAttribute<CborSerializableAttribute>();
                if (attribute?.Converter == null)
                {
                    currentType = currentType.BaseType;
                    continue;
                }

                object? converterInstance = Activator.CreateInstance(attribute.Converter);
                if (converterInstance == null) return null;

                MethodInfo? deserializeMethod = attribute.Converter.GetMethod("Deserialize",
                    BindingFlags.Public | BindingFlags.Instance,
                    null,
                    [typeof(ReadOnlyMemory<byte>)],
                    null);

                if (deserializeMethod == null) return null;

                object? result = deserializeMethod.Invoke(converterInstance, [data]);
                return result as ICbor;
            }
        }
        catch
        {
            return null;
        }

        return null;
    }

    public ICbor Deserialize(ReadOnlyMemory<byte> data)
    {
        return DeserializeWithType(data, typeof(ICbor));
    }
}