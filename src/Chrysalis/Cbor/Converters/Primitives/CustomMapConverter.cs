using System.Formats.Cbor;
using System.Reflection;
using Chrysalis.Cbor.Attributes;
using Chrysalis.Cbor.Types;
using Chrysalis.Cbor.Utils;

namespace Chrysalis.Cbor.Converters.Primitives;

public class CustomMapConverter : ICborConverter
{
    public T Deserialize<T>(byte[] data) where T : CborBase
    {
        CborReader reader = new(data);
        Type targetType = typeof(T);

        // Get properties with their CBOR property names
        List<(int? Index, string Name, Type Type)> parametersOrProperties = AssemblyUtils.GetCborPropertiesOrParameters(targetType).ToList();
        object?[] constructorArgs = new object[parametersOrProperties.Count];

        // Create mappings for both string and integer keys
        Dictionary<string, int> stringKeyMap = parametersOrProperties
            .Select((prop, index) => (prop.Name, index))
            .ToDictionary(x => x.Name, x => x.index);

        Dictionary<int, int> intKeyMap = parametersOrProperties
            .Select((prop, index) => (prop.Index, index))
            .Where(x => x.Index.HasValue)
            .ToDictionary(x => x.Index!.Value, x => x.index);

        // Read map start
        reader.ReadStartMap();

        // Read all key-value pairs
        while (reader.PeekState() != CborReaderState.EndMap)
        {
            CborReaderState keyState = reader.PeekState();

            (bool found, int index) = keyState switch
            {
                CborReaderState.TextString => TryReadStringKey(reader, stringKeyMap),
                CborReaderState.UnsignedInteger or CborReaderState.NegativeInteger => TryReadIntKey(reader, intKeyMap),
                _ => (false, -1)
            };

            if (!found)
            {
                reader.SkipValue();
                continue;
            }

            // Deserialize the value
            (int? Index, string Name, Type Type) parameterInfo = parametersOrProperties[index];
            Type propertyType = parameterInfo.Type;

            byte[] encodedValue = reader.ReadEncodedValue().ToArray();
            MethodInfo deserializeMethod = typeof(CborSerializer).GetMethod(nameof(CborSerializer.Deserialize))!;
            object? deserializedValue = deserializeMethod.MakeGenericMethod(propertyType)
                .Invoke(null, [encodedValue]);

            constructorArgs[index] = deserializedValue;
        }

        reader.ReadEndMap();

        // Create an instance using the resolved constructor arguments
        T instance = (T)Activator.CreateInstance(targetType, constructorArgs)!;
        instance.Raw = data;

        return instance;
    }

    public byte[] Serialize(CborBase value)
    {
        Type type = value.GetType();
        bool isDefinite = type.GetCustomAttribute<CborDefiniteAttribute>() != null;

        // Get properties with CborProperty attribute
        PropertyInfo[] properties = [.. type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.GetCustomAttribute<CborPropertyAttribute>() != null)];

        // Determine if we're using string or integer keys based on the first property
        CborPropertyAttribute? firstAttr = properties.FirstOrDefault()?.GetCustomAttribute<CborPropertyAttribute>();
        bool useStringKeys = !string.IsNullOrEmpty(firstAttr?.PropertyName);

        // Order properties appropriately
        if (!useStringKeys)
        {
            properties = [.. properties.OrderBy(p => p.GetCustomAttribute<CborPropertyAttribute>()!.Index)];
        }

        CborWriter writer = new();
        writer.WriteStartMap(isDefinite ? properties.Length : null);

        foreach (PropertyInfo property in properties)
        {
            CborPropertyAttribute? attr = property.GetCustomAttribute<CborPropertyAttribute>();

            // Write the key (either string or integer)
            if (useStringKeys)
            {
                writer.WriteTextString(attr?.PropertyName ?? property.Name.ToLowerInvariant());
            }
            else
            {
                writer.WriteInt64(attr?.Index ?? 0);
            }

            // Write the value
            object? propertyValue = property.GetValue(value);
            byte[] serialized = CborSerializer.Serialize((CborBase)propertyValue!);
            writer.WriteEncodedValue(serialized);
        }

        writer.WriteEndMap();
        return writer.Encode();
    }

    private static (bool found, int index) TryReadStringKey(CborReader reader, Dictionary<string, int> stringKeyMap)
    {
        string key = reader.ReadTextString();
        return stringKeyMap.TryGetValue(key, out int index) ? (true, index) : (false, -1);
    }

    private static (bool found, int index) TryReadIntKey(CborReader reader, Dictionary<int, int> intKeyMap)
    {
        int key = (int)reader.ReadInt64();
        return intKeyMap.TryGetValue(key, out int index) ? (true, index) : (false, -1);
    }
}