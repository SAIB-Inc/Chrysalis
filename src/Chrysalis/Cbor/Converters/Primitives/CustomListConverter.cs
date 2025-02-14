using System.Formats.Cbor;
using Chrysalis.Cbor.Types;

namespace Chrysalis.Cbor.Converters.Primitives;

public class CustomListConverter : ICborConverter
{
    public object Deserialize(CborReader reader, CborOptions? options = null)
    {
        reader.ReadStartArray();
        List<object> values = [];

        // Get ordered property types from options
        Dictionary<int, Type> propertyTypes = options?.PropertyIndexTypes ??
            throw new InvalidOperationException("Property types not specified in options");

        int maxProperties = options?.Size ?? propertyTypes.Count;
        for (int i = 0; i < maxProperties && reader.PeekState() != CborReaderState.EndArray; i++)
        {
            // Get type and options for this index
            if (!propertyTypes.TryGetValue(i, out Type? propertyType))
            {
                throw new InvalidOperationException($"No type found for index {i}");
            }

            CborOptions propertyOptions = CborSerializer.GetOptions(propertyType)!;
            object? value = CborSerializer.Deserialize(reader, propertyOptions);
            values.Add(value!);
        }

        while (reader.PeekState() != CborReaderState.EndArray)
        {
            reader.SkipValue();
        }
        reader.ReadEndArray();
        return values;
    }

    public void Serialize(CborWriter writer, object value, CborOptions? options = null)
    {
        object[] properties = CborSerializer.GetSortedProperties(value);
        writer.WriteStartArray(options?.IsDefinite == true ? properties.Length : null);
        foreach (object prop in properties)
        {
            CborSerializer.Serialize(writer, prop);
        }
        writer.WriteEndArray();
    }
}