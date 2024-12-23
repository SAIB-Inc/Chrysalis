using System.Formats.Cbor;
using System.Reflection;
using Chrysalis.Cbor.Attributes;
using Chrysalis.Cbor.Types;
using Chrysalis.Cbor.Utils;

namespace Chrysalis.Cbor.Converters.Primitives;

public class BytesConverter : ICborConverter
{
    public T Deserialize<T>(byte[] data) where T : CborBase
    {
        CborReader reader = CborSerializer.CreateReader(data);
        CborTagUtils.ReadAndVerifyTag<T>(reader);

        byte[] value = [];

        if (reader.PeekState() == CborReaderState.StartIndefiniteLengthByteString)
        {
            reader.ReadStartIndefiniteLengthByteString();

            List<byte[]> chunks = [];
            while (reader.PeekState() != CborReaderState.EndIndefiniteLengthByteString) chunks.Add(reader.ReadByteString());
            reader.ReadEndIndefiniteLengthByteString();

            value = [.. chunks.SelectMany(x => x)];
        }
        else
        {
            value = reader.ReadByteString();
        }

        // Use reflection to find a constructor that accepts byte[]
        ConstructorInfo constructor = typeof(T).GetConstructor([typeof(byte[])])
            ?? throw new InvalidOperationException($"Type {typeof(T).Name} does not have a constructor that accepts a byte[].");

        T instance = (T)constructor.Invoke([value]);
        instance.Raw = data;

        return instance;
    }

    public byte[] Serialize(CborBase data)
    {
        Type type = data.GetType();
        bool isDefinite = type.GetCustomAttribute<CborDefiniteAttribute>() is not null;
        CborSizeAttribute? sizeAttr = type.GetCustomAttribute<CborSizeAttribute>();
        PropertyInfo? byteProperty = type.GetProperties().FirstOrDefault(p => p.PropertyType == typeof(byte[]) && p.Name != nameof(CborBase.Raw))
            ?? throw new InvalidOperationException("No byte[] property found in Cbor object.");

        object? rawValue = byteProperty.GetValue(data);

        if (rawValue is not byte[] v) throw new InvalidOperationException("Failed to serialize byte[] property.");

        CborWriter writer = new();
        CborTagUtils.WriteTagIfPresent(writer, type);

        if (isDefinite && sizeAttr is not null)
        {
            writer.WriteStartIndefiniteLengthByteString();
            v.Chunk(sizeAttr.Size).ToList().ForEach(writer.WriteByteString);
            writer.WriteEndIndefiniteLengthByteString();
        }
        else
        {
            writer.WriteByteString(v);
        }

        return writer.Encode();
    }
}