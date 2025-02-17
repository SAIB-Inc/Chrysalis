using System.Formats.Cbor;
using Chrysalis.Cbor.Serialization;
using Chrysalis.Cbor.Serialization.Registry;

namespace Chrysalis.Cbor.Utils;

public static class CustomListSerializationUtil
{
    public static object? Read(CborReader reader, CborOptions options)
    {
        // A custom list is a special case of list where the constructor may have
        // parameters with different types. Each parameter must have a CborIndex attribute
        // to indicate the index of the parameter in the list.
        if (options.IndexPropertyMapping is null || options.IndexPropertyMapping.Count == 0)
            throw new InvalidOperationException("Index property mapping is not defined in options.");

        reader.ReadStartArray();

        List<object?> items = [];
        for (int i = 0; i < options.IndexPropertyMapping.Count && reader.PeekState() != CborReaderState.EndArray; i++)
        {
            if (!options.IndexPropertyMapping.TryGetValue(i, out Type? innerType))
                throw new InvalidOperationException($"No type found for index {i}");

            CborOptions innerOptions = CborRegistry.Instance.GetOptions(innerType);
            object? item = CborSerializer.Deserialize(reader, innerOptions);
            items.Add(item);
        }

        reader.ReadEndArray();

        return items;
    }
}