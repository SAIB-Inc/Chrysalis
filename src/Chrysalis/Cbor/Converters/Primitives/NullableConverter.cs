using System.Formats.Cbor;
using Chrysalis.Cbor.Types;
using System.Reflection;
using Chrysalis.Cbor.Utils;

namespace Chrysalis.Cbor.Converters.Primitives;

public class NullableConverter : ICborConverter
{
    public T Deserialize<T>(byte[] data) where T : CborBase
    {
        CborReader reader = new(data);
        CborTagUtils.ReadAndVerifyTag<T>(reader);

        Type targetType = typeof(T);
        Type valueType = targetType.GetGenericArguments()[0];

        ConstructorInfo constructor = targetType.GetConstructor([valueType])
            ?? throw new InvalidOperationException($"Type {targetType.Name} does not have a suitable constructor.");

        object? value;
        if (reader.PeekState() == CborReaderState.Null)
        {
            reader.ReadNull();
            value = null;
        }
        else
        {
            value = typeof(CborSerializer)
                .GetMethod(nameof(CborSerializer.Deserialize))!
                .MakeGenericMethod(valueType)
                .Invoke(null, [data]);
        }

        T instance = (T)constructor.Invoke([value]);
        instance.Raw = data;
        return instance;
    }

    public byte[] Serialize(CborBase data)
    {
        Type valueType = data.GetType().GetGenericArguments()[0];
        PropertyInfo? valueProperty = data.GetType().GetProperties()
            .FirstOrDefault(p => p.PropertyType == valueType && p.Name != nameof(CborBase.Raw))
            ?? throw new InvalidOperationException($"No property of type {valueType.Name} found in Cbor object.");

        object? value = valueProperty.GetValue(data);

        if (value is null)
        {
            CborWriter writer = new();
            CborTagUtils.WriteTagIfPresent(writer, valueType);
            writer.WriteNull();
            return writer.Encode();
        }
        else
        {
            return CborSerializer.Serialize((CborBase)value);
        }
    }
}