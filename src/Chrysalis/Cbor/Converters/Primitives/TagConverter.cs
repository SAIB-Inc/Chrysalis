using System.Formats.Cbor;
using System.Reflection;
using Chrysalis.Cbor.Attributes;
using Chrysalis.Cbor.Types;

namespace Chrysalis.Cbor.Converters;

public class TagConverter : ICborConverter
{
    public T Deserialize<T>(byte[] data) where T : CborBase
    {
        CborReader reader = new(data);
        var tag = reader.ReadTag();
        
        Type valueType = typeof(T).IsGenericType 
            ? typeof(T).GetGenericArguments()[0]
            : typeof(T).GetConstructors().First().GetParameters()[0].ParameterType;
        
        ConstructorInfo constructor = typeof(T).GetConstructor([valueType])
            ?? throw new InvalidOperationException($"Type {typeof(T).Name} does not have a constructor that accepts {valueType.Name}.");
        
        ReadOnlyMemory<byte> encodedValue = reader.ReadEncodedValue();

        object value = typeof(CborSerializer)
                .GetMethod(nameof(CborSerializer.Deserialize))!
                .MakeGenericMethod(valueType)
                .Invoke(null, [encodedValue.ToArray()])
            ?? throw new InvalidOperationException($"Failed to deserialize {valueType.Name}.");
        
        var instance = (T)constructor.Invoke([value]);
        instance.Raw = data;
        
        return (T)constructor.Invoke([value]);
    }

    public byte[] Serialize(CborBase data)
    {
        Type type = data.GetType();
        
        CborIndexAttribute indexAttr = type.GetCustomAttribute<CborIndexAttribute>()
            ?? throw new InvalidOperationException($"Type {type.Name} does not have CborIndex attribute.");
            
        PropertyInfo? valueProperty = type.GetProperties().FirstOrDefault(p => p.PropertyType.IsSubclassOf(typeof(CborBase)))
            ?? throw new InvalidOperationException("No CborBase property found in the tag object.");

        object? tagContent = valueProperty.GetValue(data)
            ?? throw new InvalidOperationException("Tag value cannot be null.");
            
        CborWriter writer = new();
        writer.WriteTag((CborTag)indexAttr.Index);
        
        if (tagContent is CborBase cborContent)
        {
            byte[] contentBytes = CborSerializer.Serialize(cborContent);
            writer.WriteEncodedValue(contentBytes);
            return writer.Encode();
        }
        else
        {
            throw new InvalidOperationException($"Value in {type.Name} does not inherit from CborBase.");
        }
    }
} 