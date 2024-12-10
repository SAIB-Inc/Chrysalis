using System.Formats.Cbor;
using System.Reflection;
using Chrysalis.Cbor.Types;
using Chrysalis.Cbor.Utils;

namespace Chrysalis.Cbor.Converters.Primitives;

public class UlongConverter : ICborConverter
{
    public T Deserialize<T>(byte[] data) where T : CborBase
    {
        CborReader reader = new(data);
        CborTagUtils.ReadAndVerifyTag<T>(reader);

        ulong value = reader.ReadUInt64();

        // Use reflection to create an instance of T
        ConstructorInfo constructor = typeof(T).GetConstructor([typeof(ulong)])
            ?? throw new InvalidOperationException($"Type {typeof(T).Name} does not have a constructor that accepts a ulong.");

        T instance = (T)constructor.Invoke([value]);
        instance.Raw = data;

        // Dynamically create the instance of T
        return instance;
    }

    public byte[] Serialize(CborBase data)
    {
        Type type = data.GetType();
        PropertyInfo? ulongProperty = data.GetType().GetProperties().FirstOrDefault(p => p.PropertyType == typeof(ulong) && p.Name != nameof(CborBase.Raw))
            ?? throw new InvalidOperationException("No ulong property found in Cbor object.");

        object? rawValue = ulongProperty.GetValue(data);

        if (rawValue is not ulong v) throw new InvalidOperationException("Failed to serialize ulong property.");

        CborWriter writer = new();
        CborTagUtils.WriteTagIfPresent(writer, type);
        writer.WriteUInt64(v);
        return writer.Encode();
    }
}