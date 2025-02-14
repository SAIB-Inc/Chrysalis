using System.Collections;
using System.Formats.Cbor;
using Chrysalis.Cbor.Types;

namespace Chrysalis.Cbor.Converters.Primitives;

public class ListConverter : ICborConverter
{
    public object Deserialize(CborReader reader, CborOptions? options = null)
    {
        reader.ReadStartArray();
        List<object?> list = [];
        Type underlyingType = options?.ActivatorType?.GetGenericArguments()[0] ??
            throw new InvalidOperationException("Underlying type not specified in options");
        CborOptions underlyingOptions = CborSerializer.GetOptions(underlyingType) ??
            throw new InvalidOperationException("Underlying options not found");
        while (reader.PeekState() != CborReaderState.EndArray)
        {
            object? item = CborSerializer.Deserialize(reader, underlyingOptions);
            list.Add(item);
        }
        reader.ReadEndArray();
        return list;
    }

    public void Serialize(CborWriter writer, object value, CborOptions? options = null)
    {
        ICollection collection = (ICollection)value;
        writer.WriteStartArray(options?.IsDefinite == true ? collection.Count : null);
        foreach (object? item in collection)
        {
            CborSerializer.Serialize(writer, item);
        }
        writer.WriteEndArray();
    }
}