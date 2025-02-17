using System.Collections;
using System.Formats.Cbor;
using System.Reflection;
using Chrysalis.Cbor.Serialization;
using Chrysalis.Cbor.Serialization.Registry;

namespace Chrysalis.Cbor.Utils;

public static class MapSerializationUtil
{

    public static List<KeyValuePair<object, object?>> ReadKeyValuePairs(CborReader reader, CborOptions options)
    {
        List<KeyValuePair<object, object?>> entries = [];
        HashSet<object> seenKeys = [];
        (Type KeyType, Type ValueType) genericTypes = GetGenericMapTypes(options);

        reader.ReadStartMap();
        while (reader.PeekState() != CborReaderState.EndMap)
        {
            object? key = CborSerializer.Deserialize(reader, CborRegistry.Instance.GetOptions(genericTypes.KeyType));
            object? value = CborSerializer.Deserialize(reader, CborRegistry.Instance.GetOptions(genericTypes.ValueType));

            if (key != null && seenKeys.Add(key))  // Add returns false if key already exists
                entries.Add(new KeyValuePair<object, object?>(key, value));
        }
        reader.ReadEndMap();

        return entries;
    }

    public static (Type KeyType, Type ValueType) GetGenericMapTypes(CborOptions options)
    {
        ParameterInfo parameter = options.Constructor!.GetParameters()
            .First(p => p.ParameterType.IsGenericType &&
                       p.ParameterType.GetGenericTypeDefinition() == typeof(Dictionary<,>));

        Type[] genericArgs = parameter.ParameterType.GetGenericArguments();
        return (genericArgs[0], genericArgs[1]);
    }

    public static object CreateMapInstance(List<KeyValuePair<object, object?>> entries, CborOptions options)
    {
        (Type KeyType, Type ValueType) genericTypes = GetGenericMapTypes(options);
        Type dictType = typeof(Dictionary<,>).MakeGenericType(genericTypes.KeyType, genericTypes.ValueType);
        IDictionary dict = (IDictionary)Activator.CreateInstance(dictType)!;

        foreach (KeyValuePair<object, object?> entry in entries)
        {
            dict.Add(entry.Key, entry.Value);
        }

        return options.Constructor!.Invoke([dict]);
    }
}