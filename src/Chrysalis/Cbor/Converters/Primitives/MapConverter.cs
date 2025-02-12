using System.Collections;
using System.Formats.Cbor;
using System.Reflection;
using Chrysalis.Cbor.Types;
using Chrysalis.Cbor.Utils;

namespace Chrysalis.Cbor.Converters.Primitives;

public class MapConverter : ICborConverter
{
    public object Deserialize(CborReader reader, CborOptions? options = null)
    {
        reader.ReadStartMap();
        var dictionary = new Dictionary<object, object>();

        while (reader.PeekState() != CborReaderState.EndMap)
        {
            var key = CborSerializer.Deserialize(reader);
            var value = CborSerializer.Deserialize(reader);
            dictionary.Add(key, value);
        }

        reader.ReadEndMap();
        return dictionary;
    }

    public void Serialize(CborWriter writer, object value, CborOptions? options = null)
    {
        var dictionary = (IDictionary)value;
        writer.WriteStartMap(options?.IsDefinite == true ? dictionary.Count : null);

        foreach (DictionaryEntry entry in dictionary)
        {
            CborSerializer.Serialize(writer, entry.Key);
            CborSerializer.Serialize(writer, entry.Value);
        }

        writer.WriteEndMap();
    }
}