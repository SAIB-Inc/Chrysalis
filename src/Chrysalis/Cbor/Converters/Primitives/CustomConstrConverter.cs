using System.Collections;
using System.Formats.Cbor;
using Chrysalis.Cbor.Types;

namespace Chrysalis.Cbor.Converters.Primitives;

public class CustomConstrConverter : ICborConverter
{
    public object Deserialize(CborReader reader, CborOptions? options = null)
    {
        reader.ReadStartArray();
        List<object> items = [];
        while (reader.PeekState() != CborReaderState.EndArray)
        {
            object item = CborSerializer.Deserialize(reader);
            items.Add(item);
        }
        reader.ReadEndArray();
        return items;
    }

    public void Serialize(CborWriter writer, object value, CborOptions? options = null)
    {
        IList list = (IList)value;
        writer.WriteStartArray(options?.IsDefinite == true ? list.Count : null);
        foreach (object? item in list)
        {
            CborSerializer.Serialize(writer, item);
        }
        writer.WriteEndArray();
    }
}