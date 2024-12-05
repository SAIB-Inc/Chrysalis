using System.Formats.Cbor;
using System.Reflection;
using Chrysalis.Types;

namespace Chrysalis.Converters;

public class UlongConverter : ICborConverter
{
    public T Deserialize<T>(byte[] data) where T : Cbor
    {
        CborReader reader = new(data);
        ulong value = reader.ReadUInt64();

        // Use reflection to create an instance of T
        ConstructorInfo constructor = typeof(T).GetConstructor([typeof(ulong)])
            ?? throw new InvalidOperationException($"Type {typeof(T).Name} does not have a constructor that accepts a ulong.");

        Cbor instance = (T)constructor.Invoke([value]);
        instance.Raw = data;

        // Dynamically create the instance of T
        return (T)constructor.Invoke([value]);
    }

    public byte[] Serialize(Cbor data)
    {
        PropertyInfo? ulongProperty = data.GetType().GetProperties().FirstOrDefault(p => p.PropertyType == typeof(ulong) && p.Name != nameof(Cbor.Raw))
            ?? throw new InvalidOperationException("No ulong property found in Cbor object.");

        object? rawValue = ulongProperty.GetValue(data);

        if (rawValue is not ulong v) throw new InvalidOperationException("Failed to serialize ulong property.");

        CborWriter writer = new();
        writer.WriteUInt64(v);
        return writer.Encode();
    }
}