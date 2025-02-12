using System.Formats.Cbor;
using Chrysalis.Cbor.Types;

namespace Chrysalis.Cbor.Converters.Primitives;
public class CustomListConverter : ICborConverter
{
    public object Deserialize(CborReader reader, CborOptions? options = null)
    {
        reader.ReadStartArray();
        var values = new List<object>();

        // Read until end or max properties
        var maxProperties = options?.Size ?? int.MaxValue;
        for (int i = 0; i < maxProperties && reader.PeekState() != CborReaderState.EndArray; i++)
        {
            var value = CborSerializer.Deserialize(reader);
            values.Add(value);
        }

        // Skip any remaining values if we hit max size
        while (reader.PeekState() != CborReaderState.EndArray)
        {
            reader.SkipValue();
        }

        reader.ReadEndArray();
        return values;
    }

    public void Serialize(CborWriter writer, object value, CborOptions? options = null)
    {
        var properties = CborSerializer.GetSortedProperties(value);
        writer.WriteStartArray(options?.IsDefinite == true ? properties.Length : null);

        foreach (var prop in properties)
        {
            CborSerializer.Serialize(writer, prop);
        }

        writer.WriteEndArray();
    }
}