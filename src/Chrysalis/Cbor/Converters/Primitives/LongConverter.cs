using System.Formats.Cbor;
using System.Reflection;
using Chrysalis.Cbor.Types;

namespace Chrysalis.Cbor.Converters.Primitives;

public class LongConverter : ICborConverter
{

    public T Deserialize<T>(byte[] data) where T : CborBase
    {
        CborReader reader = new(data);
        long value = reader.ReadInt64();

        // Use reflection to create an instance of T
        ConstructorInfo constructor = typeof(T).GetConstructor([typeof(long)])
            ?? throw new InvalidOperationException($"Type {typeof(T).Name} does not have a constructor that accepts a long.");

        CborBase instance = (T)constructor.Invoke([value]);
        instance.Raw = data;

        // Dynamically create the instance of T
        return (T)constructor.Invoke([value]);
    }

    public byte[] Serialize(CborBase data)
    {
        PropertyInfo? longProperty = data.GetType().GetProperties().FirstOrDefault(p => p.PropertyType == typeof(long) && p.Name != nameof(CborBase.Raw))
            ?? throw new InvalidOperationException("No long property found in Cbor object.");

        object? rawValue = longProperty.GetValue(data);

        if (rawValue is not int v) throw new InvalidOperationException("Failed to serialize long property.");

        CborWriter writer = new();
        writer.WriteInt64(v);
        return writer.Encode();
    }
}