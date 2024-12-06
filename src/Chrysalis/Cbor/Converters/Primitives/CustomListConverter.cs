using System.Formats.Cbor;
using System.Reflection;
using Chrysalis.Cbor.Attributes;
using Chrysalis.Cbor.Types;
using Chrysalis.Cbor.Utils;

namespace Chrysalis.Cbor.Converters.Primitives;

public class CustomListConverter : ICborConverter
{
    public T Deserialize<T>(byte[] data) where T : CborBase
    {
        CborReader reader = new(data);

        Type targetType = typeof(T);

        // Use the helper to get constructor parameters or properties
        List<(int? Index, string Name, Type Type)> parametersOrProperties = AssemblyUtils.GetCborPropertiesOrParameters(targetType).ToList();
        object?[] constructorArgs = new object[parametersOrProperties.Count];

        // Read array start
        reader.ReadStartArray();

        for (int i = 0; i < parametersOrProperties.Count; i++)
        {
            (int? Index, string Name, Type ParameterType) = parametersOrProperties[i];

            // Deserialize the value
            byte[] encodedValue = reader.ReadEncodedValue().ToArray();
            MethodInfo deserializeMethod = typeof(CborSerializer).GetMethod(nameof(CborSerializer.Deserialize))!;
            object? deserializedValue = deserializeMethod.MakeGenericMethod(ParameterType)
                .Invoke(null, [encodedValue]);

            constructorArgs[i] = deserializedValue;
        }

        reader.ReadEndArray();

        // Create an instance using the resolved constructor arguments
        T instance = (T)Activator.CreateInstance(targetType, constructorArgs)!;
        instance.Raw = data;

        return instance;
    }


    public byte[] Serialize(CborBase value)
    {
        Type type = value.GetType();
        bool isDefinite = type.GetCustomAttribute<CborDefiniteAttribute>() != null;

        PropertyInfo[] properties = [.. type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Where(p => p.GetCustomAttribute<CborPropertyAttribute>() != null)
                .OrderBy(p => p.GetCustomAttribute<CborPropertyAttribute>()?.Index)];

        CborWriter writer = new();

        // Write array start
        writer.WriteStartArray(isDefinite ? properties.Length : null);

        // Serialize properties
        foreach (PropertyInfo property in properties)
        {
            object? propertyValue = property.GetValue(value);
            byte[] serialized = CborSerializer.Serialize((CborBase)propertyValue!);
            writer.WriteEncodedValue(serialized);
        }

        writer.WriteEndArray();
        return writer.Encode();
    }
}