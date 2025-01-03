using System.Formats.Cbor;
using System.Reflection;
using Chrysalis.Cbor.Types;
using Chrysalis.Cbor.Types.Primitives;
using Chrysalis.Cbor.Utils;


namespace Chrysalis.Cbor.Converters.Primitives;

public class BoolConverter : ICborConverter
{
    public T Deserialize<T>(ReadOnlyMemory<byte> data) where T : CborBase
    {
        CborReader reader = CborSerializer.CreateReader(data);

        if (reader.PeekState() != CborReaderState.Boolean)
            throw new InvalidOperationException($"Error at type {typeof(T).Name} for property {typeof(T).GetProperties().First().Name} => Expected Boolean but got {reader.PeekState()}");

        bool value = reader.ReadBoolean();

        // Read and verify the tag
        CborTagUtils.ReadAndVerifyTag<T>(reader);

        // Use reflection to create an instance of T
        ConstructorInfo constructor = typeof(T).GetConstructor([typeof(bool)])
            ?? throw new InvalidOperationException($"Type {typeof(T).Name} does not have a constructor that accepts a bool.");

        T instance = (T)constructor.Invoke([value]);
        instance.Raw = data;

        // Dynamically create the instance of T
        return instance;
    }

    public byte[] Serialize(CborBase data)
    {
        Type type = data.GetType();

        // Find the property representing the bool value
        PropertyInfo? boolProperty = type.GetProperties().FirstOrDefault(p => p.PropertyType == typeof(bool))
            ?? throw new InvalidOperationException("No boolean property found in the Cbor object.");

        // Use the actual instance (data) to get the property value
        object? rawValue = boolProperty.GetValue(data);

        if (rawValue is not bool v) throw new InvalidOperationException("Failed to serialize bool property.");

        CborWriter writer = new();
        CborTagUtils.WriteTagIfPresent(writer, type);
        writer.WriteBoolean(v);
        return writer.Encode();
    }
}