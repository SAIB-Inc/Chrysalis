using System.Formats.Cbor;
using System.Reflection;
using Chrysalis.Attributes;
using Chrysalis.Types;

namespace Chrysalis.Converters;

public class ListConverter<T> : ICborConverter<CborList<T>> where T : ICbor
{
    public byte[] Serialize(CborList<T> value)
    {
        CborWriter writer = new();

        Type type = value.GetType();
        bool isDefinite = type.GetCustomAttribute<CborDefiniteAttribute>() != null;

        writer.WriteStartArray(isDefinite ? value.Value.Count() : null);

        // Now we can just use CborSerializer for each item
        foreach (T item in value.Value)
        {
            byte[] serialized = CborSerializer.Serialize(item);
            writer.WriteEncodedValue(serialized);
        }

        writer.WriteEndArray();
        return [.. writer.Encode()];
    }

    public ICbor Deserialize(byte[] data, Type? targetType = null)
    {
        CborReader reader = new(data);
        reader.ReadStartArray();

        List<T> list = [];

        while (reader.PeekState() != CborReaderState.EndArray)
        {
            byte[] itemBytes = reader.ReadEncodedValue().ToArray();
            T item = CborSerializer.Deserialize<T>(itemBytes);
            list.Add(item);
        }

        reader.ReadEndArray();
        return new CborList<T>(list);
    }
}