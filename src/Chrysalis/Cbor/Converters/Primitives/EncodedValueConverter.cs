using System.Formats.Cbor;
using System.Reflection;
using Chrysalis.Cbor.Types;

namespace Chrysalis.Cbor.Converters.Primitives;

public class EncodedValueConverter : ICborConverter
{
    public T Deserialize<T>(byte[] data) where T : CborBase
    {
        CborReader reader = new(data);
        CborTag tag = reader.ReadTag();

        if (tag != CborTag.EncodedCborDataItem)
            throw new InvalidOperationException($"Expected EncodedCborDataItem tag, got {tag}");

        byte[] value = reader.ReadByteString();

        // Use reflection to create an instance of T
        ConstructorInfo constructor = typeof(T).GetConstructor([typeof(byte[])])
            ?? throw new InvalidOperationException($"Type {typeof(T).Name} does not have a constructor that accepts a byte[].");

        CborBase instance = (T)constructor.Invoke([value]);
        instance.Raw = data;

        // Dynamically create the instance of T
        return (T)constructor.Invoke([value]);
    }

    public byte[] Serialize(CborBase data)
    {
        PropertyInfo? byteProperty = data.GetType().GetProperties().FirstOrDefault(p => p.PropertyType == typeof(byte[]) && p.Name != nameof(CborBase.Raw))
            ?? throw new InvalidOperationException("No byte[] property found in Cbor object.");

        object? rawValue = byteProperty.GetValue(data);

        if (rawValue is not byte[] v) throw new InvalidOperationException("Failed to serialize byte[] property.");

        CborWriter writer = new();
        writer.WriteTag(CborTag.EncodedCborDataItem);
        writer.WriteByteString(v);
        return writer.Encode();
    }
}