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
        CborReader reader = CborSerializer.CreateReader(data);
        CborTagUtils.ReadAndVerifyTag<T>(reader);

        Type targetType = typeof(T);

        // Use the helper to get constructor parameters or properties
        List<(int? Index, string Name, Type Type)> parametersOrProperties = [.. AssemblyUtils.GetCborPropertiesOrParameters(targetType)];
        object?[] constructorArgs = new object[parametersOrProperties.Count];

        // Read array start
        reader.ReadStartArray();

        for (int i = 0; i < parametersOrProperties.Count; i++)
        {
            (int? Index, string Name, Type ParameterType) = parametersOrProperties[i];

            if (reader.PeekState() == CborReaderState.EndArray)
            {
                if (IsOptionalType(ParameterType) || HasOptionalAttribute(parametersOrProperties[i]))
                {
                    constructorArgs[i] = null;
                    continue;
                }

                throw new Exception($"Missing required value for {Name}");
            }

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

    private static bool IsNullableType(Type type)
    {
        // Check if it's a nullable value type
        if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(Nullable<>))
            return true;

        // Check if it's a reference type
        if (!type.IsValueType)
            return true;

        return false;
    }

    private static bool IsOptionalType(Type type)
    {
        // Check for nullable value types
        if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(Nullable<>))
            return true;

        // Check for reference types
        if (!type.IsValueType)
            return true;

        return false;
    }

    private static bool HasOptionalAttribute((int? Index, string Name, Type Type) parameter)
    {
        // Get the parameter info from the declaring type
        var memberInfo = parameter.Type.DeclaringType?
            .GetMembers()
            .FirstOrDefault(m => m.Name == parameter.Name);

        if (memberInfo == null)
            return false;

        // Check for any attributes that indicate optionality
        // You can extend this to check for your specific attributes
        return memberInfo.GetCustomAttributes(true)
            .Any(attr => attr.GetType().Name.Contains("Optional") ||
                         attr.GetType().Name.Contains("Nullable"));
    }

    public byte[] Serialize(CborBase value)
    {
        Type type = value.GetType();
        bool isDefinite = type.GetCustomAttribute<CborDefiniteAttribute>() != null;

        PropertyInfo[] properties = [.. type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Where(p => p.GetCustomAttribute<CborPropertyAttribute>() != null)
                .OrderBy(p => p.GetCustomAttribute<CborPropertyAttribute>()?.Index)];

        CborWriter writer = new();
        CborTagUtils.WriteTagIfPresent(writer, type);

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