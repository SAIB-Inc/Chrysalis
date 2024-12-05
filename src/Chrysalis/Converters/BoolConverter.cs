using System.Formats.Cbor;
using System.Reflection;
using Chrysalis.Attributes;
using Chrysalis.Types;

namespace Chrysalis.Converters;

public class BoolConverter : ICborConverter
{
    public T Deserialize<T>(byte[] data) where T : Cbor
    {
        CborReader reader = new(data);
        bool value = reader.ReadBoolean();

        // Use reflection to create an instance of T
        ConstructorInfo constructor = typeof(T).GetConstructor([typeof(bool)])
            ?? throw new InvalidOperationException($"Type {typeof(T).Name} does not have a constructor that accepts a bool.");

        Cbor instance = (T)constructor.Invoke([value]);
        instance.Raw = data;

        // Dynamically create the instance of T
        return (T)constructor.Invoke([value]);
    }

    public byte[] Serialize(Cbor data)
    {
        Type type = data.GetType();

        // Find the property representing the bool value
        PropertyInfo? boolProperty = type.GetProperties().FirstOrDefault(p => p.PropertyType == typeof(bool))
            ?? throw new InvalidOperationException("No boolean property found in the Cbor object.");

        // Use the actual instance (data) to get the property value
        object? rawValue = boolProperty.GetValue(data);

        if (rawValue is not bool v) throw new InvalidOperationException("Failed to serialize bool property.");

        CborWriter writer = new();
        writer.WriteBoolean(v);
        return writer.Encode();
    }
}