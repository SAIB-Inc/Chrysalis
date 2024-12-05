using System.Formats.Cbor;
using System.Reflection;
using Chrysalis.Types;

namespace Chrysalis.Converters;

public class EncodedValueConverter : ICborConverter
{
    // public byte[] Serialize(CborEncodedValue value)
    // {
    //     CborWriter writer = new();

    //     // Write tag for encoded CBOR data item
    //     writer.WriteTag(CborTag.EncodedCborDataItem);

    //     // Write the encoded bytes as a byte string
    //     writer.WriteByteString(value.Value);

    //     return [.. writer.Encode()];
    // }

    // public ICbor Deserialize(byte[] data, Type? targetType = null)
    // {
    //     CborReader reader = new CborReader(data);

    //     // Read and verify tag
    //     CborTag tag = reader.ReadTag();
    //     if (tag != CborTag.EncodedCborDataItem)
    //         throw new InvalidOperationException($"Expected EncodedCborDataItem tag, got {tag}");

    //     // Read the byte string
    //     return new CborEncodedValue(reader.ReadByteString());
    // }
    public T Deserialize<T>(byte[] data) where T : Cbor
    {
        CborReader reader = new(data);
        CborTag tag = reader.ReadTag();

        if (tag != CborTag.EncodedCborDataItem)
            throw new InvalidOperationException($"Expected EncodedCborDataItem tag, got {tag}");

        byte[] value = reader.ReadByteString();

        // Use reflection to create an instance of T
        ConstructorInfo constructor = typeof(T).GetConstructor([typeof(byte[])])
            ?? throw new InvalidOperationException($"Type {typeof(T).Name} does not have a constructor that accepts a byte[].");

        Cbor instance = (T)constructor.Invoke([value]);
        instance.Raw = data;

        // Dynamically create the instance of T
        return (T)constructor.Invoke([value]);
    }

    public byte[] Serialize(Cbor data)
    {
        PropertyInfo? byteProperty = data.GetType().GetProperties().FirstOrDefault(p => p.PropertyType == typeof(byte[]) && p.Name != nameof(Cbor.Raw))
            ?? throw new InvalidOperationException("No byte[] property found in Cbor object.");

        object? rawValue = byteProperty.GetValue(data);

        if (rawValue is not byte[] v) throw new InvalidOperationException("Failed to serialize byte[] property.");

        CborWriter writer = new();
        writer.WriteTag(CborTag.EncodedCborDataItem);
        writer.WriteByteString(v);
        return writer.Encode();
    }
}