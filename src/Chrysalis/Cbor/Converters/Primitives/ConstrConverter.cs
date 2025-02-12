using System.Formats.Cbor;
using Chrysalis.Cbor.Types;

namespace Chrysalis.Cbor.Converters.Primitives;

public class ConstrConverter : ICborConverter
{
    public void Serialize(CborWriter writer, object value, CborOptions? options = null)
    {
        object[] propertyValues = CborSerializer.GetPropertyValues(value, options?.Index ?? 0);
        writer.WriteStartArray(options?.IsDefinite == true ? propertyValues.Length : null);
        foreach (object propertyValue in propertyValues)
        {
            CborSerializer.Serialize(writer, propertyValue);
        }
        writer.WriteEndArray();
    }

    public object Deserialize(CborReader reader, CborOptions? options = null)
    {
        reader.ReadStartArray();

        List<object> values = [];
        while (reader.PeekState() != CborReaderState.EndArray)
        {
            object value = CborSerializer.Deserialize(reader);
            values.Add(value);
        }
        reader.ReadEndArray();
        return values.ToArray();
    }
}