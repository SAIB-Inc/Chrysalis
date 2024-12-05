using System.Formats.Cbor;
using Chrysalis.Types;
using System.Reflection;
using System.ComponentModel;

namespace Chrysalis.Converters;

public class NullableConverter : ICborConverter
{
    public T Deserialize<T>(byte[] data) where T : Cbor
    {
        CborReader reader = new(data);
        Type targetType = typeof(T);
        Type valueType = targetType.GetGenericArguments()[0];

        ConstructorInfo constructor = targetType.GetConstructor([valueType])
            ?? throw new InvalidOperationException($"Type {targetType.Name} does not have a suitable constructor.");

        if (reader.PeekState() == CborReaderState.Null)
        {
            reader.ReadNull();
            Cbor instance = (T)constructor.Invoke([null]);
            instance.Raw = data;
            return (T)instance;
        }
        else
        {
            var innerValue = typeof(CborSerializer)
                .GetMethod(nameof(CborSerializer.Deserialize))!
                .MakeGenericMethod(valueType)
                .Invoke(null, [data]);
                
            Cbor instance = (T)constructor.Invoke([innerValue]);
            instance.Raw = data;
            return (T)instance;
        }
    }

    public byte[] Serialize(Cbor data)
    {
        CborWriter writer = new();

        Type valueType = data.GetType().GetGenericArguments()[0];
        PropertyInfo? valueProperty = data.GetType().GetProperties()
            .FirstOrDefault(p => p.PropertyType == valueType && p.Name != nameof(Cbor.Raw))
            ?? throw new InvalidOperationException($"No property of type {valueType.Name} found in Cbor object.");

        object? value = valueProperty.GetValue(data);

        if (value is null)
        {
            writer.WriteNull();
            return writer.Encode();
        }
        else
        {
            return CborSerializer.Serialize((Cbor)value);
        }
    }
}