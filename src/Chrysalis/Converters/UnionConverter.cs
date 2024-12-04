using System.Formats.Cbor;
using System.Reflection;
using Chrysalis.Attributes;
using Chrysalis.Types;

namespace Chrysalis.Converters;

public class UnionConverter<T> : ICborConverter<T> where T : ICbor
{
    public byte[] Serialize(T value)
    {
        CborWriter writer = new();

        Type type = value.GetType();

        // Get the converter for the concrete type
        CborConverterAttribute? converterAttr = type.GetCustomAttribute<CborConverterAttribute>();
        if (converterAttr != null && converterAttr.ConverterType != typeof(UnionConverter<>))
        {
            // Use the concrete type's converter
            object converterInstance = Activator.CreateInstance(converterAttr.ConverterType)
                ?? throw new InvalidOperationException($"Failed to create converter for {type.Name}");

            MethodInfo serializeMethod = converterAttr.ConverterType.GetMethod("Serialize")
                ?? throw new InvalidOperationException($"No Serialize method found for {type.Name}");

            byte[] serializedData = (byte[])serializeMethod.Invoke(converterInstance, [value])!;
            writer.WriteEncodedValue(serializedData);
            return writer.Encode();
        }

        throw new InvalidOperationException($"No converter found for type {type.Name}");
    }

    public ICbor Deserialize(byte[] data, Type? targetType = null)
    {
        CborReader reader = new(data);

        // If a target type is provided, attempt to deserialize it directly
        if (targetType != null)
        {
            return TryDeserialize(data, targetType);
        }

        // Otherwise, search all possible types that implement T
        List<Type> possibleTypes = Assembly.GetExecutingAssembly()
            .GetTypes()
            .Where(t => typeof(T).IsAssignableFrom(t) && !t.IsAbstract)
            .ToList();

        foreach (Type type in possibleTypes)
        {
            try
            {
                ICbor deserializedValue = TryDeserialize(data, type);
                if (deserializedValue != null)
                {
                    return deserializedValue;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to deserialize as {type.Name}: {ex.Message}");
            }
        }

        throw new InvalidOperationException("Unable to determine the type for deserialization.");
    }

    private static ICbor TryDeserialize(byte[] data, Type type)
    {
        // Check if the type has a specific converter
        CborConverterAttribute? converterAttr = type.GetCustomAttribute<CborConverterAttribute>();
        if (converterAttr != null)
        {
            object converterInstance = Activator.CreateInstance(converterAttr.ConverterType)
                ?? throw new InvalidOperationException($"Failed to create converter for {type.Name}");

            MethodInfo deserializeMethod = converterAttr.ConverterType.GetMethod("Deserialize")
                ?? throw new InvalidOperationException($"No Deserialize method found for {type.Name}");

            // Attempt deserialization
            return (ICbor)deserializeMethod.Invoke(converterInstance, [data, type])!;
        }

        throw new InvalidOperationException($"No converter found for type {type.Name}");
    }
}
