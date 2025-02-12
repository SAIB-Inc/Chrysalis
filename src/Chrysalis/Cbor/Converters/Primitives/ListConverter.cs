using System.Collections;
using System.Formats.Cbor;
using Chrysalis.Cbor.Types;

namespace Chrysalis.Cbor.Converters.Primitives;

public class ListConverter : ICborConverter
{
    public object Deserialize(CborReader reader, CborOptions? options = null)
    {
        reader.ReadStartArray();
        List<object> list = [];
        while (reader.PeekState() != CborReaderState.EndArray)
        {
            object item = CborSerializer.Deserialize(reader);
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