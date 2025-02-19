using System.Collections;
using System.Formats.Cbor;
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
            throw new InvalidOperationException($"Expected the first element to be a dictionary, but got {firstElement?.GetType().FullName ?? "null"}");

        writer.WriteStartMap(options.IsDefinite ? dict.Count : null);

        // Iterate over the dictionary
        foreach (DictionaryEntry entry in dict)
        {
            if (entry.Key == null)
                throw new InvalidOperationException("Dictionary key cannot be null");

            // Get type information from the actual objects
            Type keyType = entry.Key.GetType();
            Type valueType = entry.Value?.GetType() ?? typeof(object);

            CborOptions keyOptions = CborRegistry.Instance.GetOptions(keyType);
            CborOptions valueOptions = entry.Value != null
                ? CborRegistry.Instance.GetOptions(valueType)
                : CborOptions.Default;

            // Serialize key and value
            CborSerializer.Serialize(writer, entry.Key, keyOptions);
            CborSerializer.Serialize(writer, entry.Value, valueOptions);
        }

        writer.WriteEndMap();
    }
}