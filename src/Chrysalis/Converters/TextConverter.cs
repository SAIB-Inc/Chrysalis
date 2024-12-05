using System.Formats.Cbor;
using System.Reflection;
using Chrysalis.Types;

namespace Chrysalis.Converters;

public class TextConverter : ICborConverter
{
    // public ICbor Deserialize(byte[] data, Type? targetType = null)
    // {
    //     CborReader reader = new(data);
    //     return new CborText(reader.ReadTextString());
    // }
    public T Deserialize<T>(byte[] data) where T : Cbor
    {
        CborReader reader = new(data);
        string value = reader.ReadTextString();

        ConstructorInfo constructor = typeof(T).GetConstructor([typeof(string)])
            ?? throw new InvalidOperationException($"Type {typeof(T).Name} does not have a constructor that accepts a string.");

        Cbor instance = (T)constructor.Invoke([value]);
        instance.Raw = data;

        return (T)constructor.Invoke([value]);
    }

    public byte[] Serialize(Cbor data)
    {
        PropertyInfo? stringProperty = data.GetType().GetProperties().FirstOrDefault(p => p.PropertyType == typeof(string) && p.Name != nameof(Cbor.Raw))
            ?? throw new InvalidOperationException("No string property found in Cbor object.");

        object? rawValue = stringProperty.GetValue(data);

        if (rawValue is not string v) throw new InvalidOperationException("Failed to serialize string property.");

        CborWriter writer = new();
        writer.WriteTextString(v);
        return writer.Encode();
    }
}