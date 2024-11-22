using System.Reflection;
using Chrysalis.Attributes;
using Chrysalis.Types.Core;

namespace Chrysalis.Converters;

public class CborUnionConverter : ICborConverter<ICborUnion>
{
    public ICborUnion Deserialize(ReadOnlyMemory<byte> data)
    {
        List<Type> possibleTypes = Assembly.GetExecutingAssembly()
            .GetTypes()
            .Where(t => !t.IsInterface && !t.IsAbstract && typeof(ICborUnion).IsAssignableFrom(t))
            .ToList();

        foreach (Type type in possibleTypes)
        {
            ICborUnion? result = TryDeserialize(type, data);
            if (result is not null) return result;
        }

        throw new InvalidOperationException(
            $"Could not deserialize to any known type implementing ICborUnion. Attempted types: {string.Join(", ", possibleTypes.Select(t => t.Name))}");
    }

    public ReadOnlyMemory<byte> Serialize(ICborUnion data)
    {
        // Determine the actual type of the value
        Type actualType = data.GetType();

        // Retrieve the CborSerializable attribute from the actual type
        CborSerializableAttribute attribute = actualType.GetCustomAttribute<CborSerializableAttribute>()
            ?? throw new InvalidOperationException($"The type {actualType.Name} is not marked with CborSerializableAttribute.");

        // Ensure the converter is specified in the attribute
        if (attribute.Converter == null)
        {
            throw new InvalidOperationException($"No converter specified in CborSerializableAttribute for type {actualType.Name}.");
        }

        // Create an instance of the converter for the actual type
        object converter = Activator.CreateInstance(attribute.Converter) ?? throw new InvalidOperationException($"Could not create an instance of the converter {attribute.Converter.Name}.");

        // Find the Serialize method of the converter, which should match the actual type
        MethodInfo serializeMethod = attribute.Converter.GetMethod("Serialize") ?? throw new InvalidOperationException($"No Serialize method found on converter {attribute.Converter.Name}.");

        // Invoke the Serialize method with the actual value
        object? result = serializeMethod.Invoke(converter, [data]);

        if (result is not ReadOnlyMemory<byte> memoryData)
        {
            throw new InvalidOperationException($"Serialize method did not return a ReadOnlyMemory<byte>.");
        }

        // Return the serialized bytes
        return memoryData;
    }

    private static ICborUnion? TryDeserialize(Type type, ReadOnlyMemory<byte> data)
    {
        try
        {
            CborSerializableAttribute? attribute = type.GetCustomAttribute<CborSerializableAttribute>();
            if (attribute?.Converter == null) return null;

            object? converterInstance = Activator.CreateInstance(attribute.Converter);
            if (converterInstance is null) return null;

            MethodInfo? deserializeMethod = attribute.Converter.GetMethod("Deserialize",
                BindingFlags.Public | BindingFlags.Instance,
                null,
                [typeof(ReadOnlyMemory<byte>)],
                null);

            if (deserializeMethod is null) return null;

            object? result = deserializeMethod.Invoke(converterInstance, [data]);
            return result as ICborUnion;
        }
        catch
        {
            return null;
        }
    }

}