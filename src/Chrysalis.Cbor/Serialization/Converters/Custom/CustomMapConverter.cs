using System.Formats.Cbor;
using Chrysalis.Cbor.Utils;

namespace Chrysalis.Cbor.Serialization.Converters.Custom;

public sealed class CustomMapConverter : ICborConverter
{
    public object? Read(CborReader reader, CborOptions options)
    {
        CustomMapSerializationUtil.ValidateOptions(options);

        reader.ReadStartMap();
        Dictionary<object, object?> items = [];

        bool isIndexBased = options.IndexPropertyMapping?.Count > 0;

        while (reader.PeekState() != CborReaderState.EndMap)
        {
            (object? key, object? value) = CustomMapSerializationUtil.ReadKeyValuePair(reader, options, isIndexBased);

            if (key is not null)
            {
                items[key] = value;
            }
        }

        reader.ReadEndMap();
        return items;
    }

    public void Write(CborWriter writer, List<object?> value, CborOptions options)
    {
        writer.WriteStartMap(options.IsDefinite ? options.Size : null);

        if (value.Count > 0)
            CustomListSerializationUtil.Write(writer, value);

        writer.WriteEndMap();
    }
}