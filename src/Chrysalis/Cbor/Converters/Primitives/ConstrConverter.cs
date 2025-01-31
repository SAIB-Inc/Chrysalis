using System.Formats.Cbor;
using System.Reflection;
using Chrysalis.Cbor.Attributes;
using Chrysalis.Cbor.Types;
using Chrysalis.Cbor.Utils;

namespace Chrysalis.Cbor.Converters.Primitives;

public class ConstrConverter : ICborConverter
{
    public T Deserialize<T>(byte[] data) where T : CborBase
    {
        CborReader reader = CborSerializer.CreateReader(data);
        CborTagUtils.ReadAndVerifyTag<T>(reader);

        Type targetType = typeof(T);

        // Use the helper to get constructor parameters or properties
        List<(int? Index, string Name, Type Type)> parametersOrProperties = AssemblyUtils.GetCborPropertiesOrParameters(targetType).ToList();

        // Read the tag and validate it
        CborTag tag = reader.ReadTag();
        CborTag expectedTag = GetTag(targetType.GetCustomAttribute<CborIndexAttribute>()?.Index ?? 0);

        if (tag != expectedTag)
        {
            throw new InvalidOperationException($"Expected tag {expectedTag}, got {tag}");
        }

        // Read array start
        reader.ReadStartArray();

        // Prepare arguments for constructor
        object?[] constructorArgs = new object[parametersOrProperties.Count];

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
        int index = type.GetCustomAttribute<CborIndexAttribute>()?.Index ?? 0;
        bool isDefinite = type.GetCustomAttribute<CborDefiniteAttribute>() != null;

        // Get the first constructor and its parameters
        ConstructorInfo constructor = type.GetConstructors(BindingFlags.Public | BindingFlags.Instance).First();
        ParameterInfo[] parameters = constructor.GetParameters();

        // Map parameters to properties
        PropertyInfo?[] properties = parameters
            .Select(param => type.GetProperty(param.Name!, BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase))
            .Where(prop => prop != null)
            .ToArray()!;

        CborWriter writer = new();
        CborTagUtils.WriteTagIfPresent(writer, type);

        writer.WriteTag(GetTag(index));
        writer.WriteStartArray(isDefinite ? properties.Length : null);

        foreach (PropertyInfo property in properties)
        {
            object? propertyValue = property.GetValue(value);
            byte[] serialized = CborSerializer.Serialize((CborBase)propertyValue!);
            writer.WriteEncodedValue(serialized);
        }

        writer.WriteEndArray();
        return writer.Encode();
    }

    private static CborTag GetTag(int index)
    {
        int finalIndex = index > 6 ? 1280 - 7 : 121;
        return (CborTag)(finalIndex + index);
    }
}