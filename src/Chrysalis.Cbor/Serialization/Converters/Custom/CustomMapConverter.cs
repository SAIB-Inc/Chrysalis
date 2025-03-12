using System.Formats.Cbor;
using Chrysalis.Cbor.Types;
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
                items[key] = value;
        }

        reader.ReadEndMap();
        return items;
    }

    public void Write(CborWriter writer, List<object?> value, CborOptions options)
    {
        int count = value.Count(v => v is not null);
        writer.WriteStartMap(options.IsDefinite ? count : null);

        bool useIndexMapping = options.IndexPropertyMapping != null && options.IndexPropertyMapping.Count > 0;

        // Write key-value pairs only for non-null properties
        for (int i = 0; i < value.Count; i++)
        {
            object? property = value[i];
            CborBase? cborProperty = property as CborBase;
            if (property == null) continue; // Skip null properties

            // Write the key based on mapping type
            if (useIndexMapping)
            {
                writer.WriteInt32(i); // Write index as key
            }
            else if (options.NamedPropertyMapping != null)
            {
                // Get property name from type's property at index i
                string? propName = options.RuntimeType?.GetProperties()
                    .Where(p => p.Name != "Raw")
                    .ElementAtOrDefault(i)?.Name;

                if (propName != null)
                    writer.WriteTextString(propName); // Write name as key
                else
                    continue; // Skip if property name not found
            }

            // Write the property value
            List<object?> filteredProperties = PropertyResolver.GetFilteredProperties(property);
            cborProperty!.Write(writer, filteredProperties);
        }

        writer.WriteEndMap();
    }
}