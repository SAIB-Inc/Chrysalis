using System.Formats.Cbor;
using Chrysalis.Cbor.Serialization;
using Chrysalis.Cbor.Serialization.Registry;

namespace Chrysalis.Cbor.Utils;

public static class CustomConstrSerializationUtil
{
    public static object? Read(CborReader reader, CborOptions options)
    {
        // In a custom constructor, we do not check for specific index
        // This converter is expected to only accept single list as constructor argument
        if (reader.PeekState() != CborReaderState.Tag)
            throw new InvalidOperationException("Custom constructor is expected to be tagged");

        reader.ReadTag();
        reader.ReadStartArray();

        if (options.RuntimeType is null)
            throw new InvalidOperationException("Runtime type is not defined in options.");

        Type innerType = options.RuntimeType.GetGenericArguments()[0];
        CborOptions innerOptions = CborRegistry.Instance.GetOptions(innerType);

        List<object?> items = [];
        while (reader.PeekState() != CborReaderState.EndArray)
        {
            object? item = CborSerializer.Deserialize(reader, innerOptions);
            items.Add(item);
        }
        reader.ReadEndArray();

        return items;
    }
}