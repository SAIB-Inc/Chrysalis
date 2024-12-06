using System.Formats.Cbor;
using System.Reflection;
using Chrysalis.Cbor.Types;

namespace Chrysalis.Cbor.Converters.Primitives;

public class IntConverter : ICborConverter
{
    public T Deserialize<T>(byte[] data) where T : CborBase
    {
        CborReader reader = new(data);
        int value = reader.ReadInt32();

        // Use reflection to create an instance of T
        ConstructorInfo constructor = typeof(T).GetConstructor([typeof(int)])
            ?? throw new InvalidOperationException($"Type {typeof(T).Name} does not have a constructor that accepts a int.");

        CborBase instance = (T)constructor.Invoke([value]);
        instance.Raw = data;

        // Dynamically create the instance of T
        return (T)constructor.Invoke([value]);
    }

    public byte[] Serialize(CborBase data)
    {
        PropertyInfo? intProperty = data.GetType().GetProperties().FirstOrDefault(p => p.PropertyType == typeof(int) && p.Name != nameof(CborBase.Raw))
            ?? throw new InvalidOperationException("No int property found in Cbor object.");

        object? rawValue = intProperty.GetValue(data);

        if (rawValue is not int v) throw new InvalidOperationException("Failed to serialize int property.");

        CborWriter writer = new();
        writer.WriteInt32(v);
        return writer.Encode();
    }
}