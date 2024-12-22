using System.Formats.Cbor;
using System.Reflection;
using Chrysalis.Cbor.Types;
using Chrysalis.Cbor.Utils;

namespace Chrysalis.Cbor.Converters.Primitives;

public class TextConverter : ICborConverter
{
    public T Deserialize<T>(byte[] data) where T : CborBase
    {
        CborReader reader = CborSerializer.CreateReader(data);
        CborTagUtils.ReadAndVerifyTag<T>(reader);

        string value = reader.ReadTextString();

        ConstructorInfo constructor = typeof(T).GetConstructor([typeof(string)])
            ?? throw new InvalidOperationException($"Type {typeof(T).Name} does not have a constructor that accepts a string.");

        T instance = (T)constructor.Invoke([value]);
        instance.Raw = data;

        return instance;
    }

    public byte[] Serialize(CborBase data)
    {
        Type type = data.GetType();
        PropertyInfo? stringProperty = data.GetType().GetProperties().FirstOrDefault(p => p.PropertyType == typeof(string) && p.Name != nameof(CborBase.Raw))
            ?? throw new InvalidOperationException("No string property found in Cbor object.");

        object? rawValue = stringProperty.GetValue(data);

        if (rawValue is not string v) throw new InvalidOperationException("Failed to serialize string property.");

        CborWriter writer = new();
        CborTagUtils.WriteTagIfPresent(writer, type);
        writer.WriteTextString(v);
        return writer.Encode();
    }
}