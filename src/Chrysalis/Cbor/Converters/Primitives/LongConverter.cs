using System.Formats.Cbor;
using System.Reflection;
using Chrysalis.Cbor.Types;
using Chrysalis.Cbor.Utils;

namespace Chrysalis.Cbor.Converters.Primitives;

public class LongConverter : ICborConverter
{

    public T Deserialize<T>(byte[] data) where T : CborBase
    {
        CborReader reader = new(data);
        CborTagUtils.ReadAndVerifyTag<T>(reader);

        long value = reader.ReadInt64();

        // Use reflection to create an instance of T
        ConstructorInfo constructor = typeof(T).GetConstructor([typeof(long)])
            ?? throw new InvalidOperationException($"Type {typeof(T).Name} does not have a constructor that accepts a long.");

        T instance = (T)constructor.Invoke([value]);
        instance.Raw = data;

        // Dynamically create the instance of T
        return instance;
    }

    public byte[] Serialize(CborBase data)
    {
        Type type = data.GetType();
        PropertyInfo? longProperty = data.GetType().GetProperties().FirstOrDefault(p => p.PropertyType == typeof(long) && p.Name != nameof(CborBase.Raw))
            ?? throw new InvalidOperationException("No long property found in Cbor object.");

        object? rawValue = longProperty.GetValue(data);

        if (rawValue is not int v) throw new InvalidOperationException("Failed to serialize long property.");

        CborWriter writer = new();
        CborTagUtils.WriteTagIfPresent(writer, type);
        writer.WriteInt64(v);
        return writer.Encode();
    }
}