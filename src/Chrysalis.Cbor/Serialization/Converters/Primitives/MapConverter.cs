using System.Collections;
using System.Formats.Cbor;
using System.Reflection;
using System.Runtime.CompilerServices;
using Chrysalis.Cbor.Serialization.Registry;
using Chrysalis.Cbor.Utils;

namespace Chrysalis.Cbor.Serialization.Converters.Primitives;

public sealed class MapConverter : ICborConverter
{
    public object Read(CborReader reader, CborOptions options)
    {
        if (options.Constructor is null)
            throw new InvalidOperationException("Constructor not specified");

        List<KeyValuePair<object, object?>> entries = MapSerializationUtil.ReadKeyValuePairs(reader, options);
        return MapSerializationUtil.CreateMapInstance(entries, options);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Write(CborWriter writer, List<object?> value, CborOptions options)
    {
        if (value == null || value.Count == 0)
            throw new InvalidOperationException("Value cannot be null or empty");

        // Expect that the first element is the dictionary.
        object? firstElement = value.First();
        if (firstElement is not IDictionary dict)
            throw new InvalidOperationException("Expected the first element to be a dictionary");

        // Get generic map types from options
        (Type KeyType, Type ValueType) genericTypes = MapSerializationUtil.GetGenericMapTypes(options.RuntimeType?.GetConstructors().First());

        writer.WriteStartMap(options.IsDefinite ? dict.Count : null);

        // Iterate over the dictionary as an IEnumerable.
        foreach (object item in dict)
        {
            DictionaryEntry entry;
            if (item is DictionaryEntry de)
            {
                entry = de;
            }
            else
            {
                // For generic dictionaries, item will be a KeyValuePair<,>
                Type type = item.GetType();
                var keyProp = type.GetProperty("Key");
                var valueProp = type.GetProperty("Value");

                if (keyProp == null || valueProp == null)
                    throw new InvalidOperationException("Dictionary item does not have Key and Value properties.");

                object key = keyProp.GetValue(item) ?? throw new InvalidOperationException("Key cannot be null");
                object? val = valueProp.GetValue(item);
                entry = new DictionaryEntry(key, val);
            }

            CborOptions keyOptions = CborRegistry.Instance.GetOptions(genericTypes.KeyType);
            CborOptions valueOptions = CborRegistry.Instance.GetOptions(genericTypes.ValueType);

            CborSerializer.Serialize(writer, entry.Key, keyOptions);
            CborSerializer.Serialize(writer, entry.Value, valueOptions);
        }

        writer.WriteEndMap();
    }
}