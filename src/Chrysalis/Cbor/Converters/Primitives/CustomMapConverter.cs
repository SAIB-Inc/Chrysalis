using Chrysalis.Cbor.Types;

namespace Chrysalis.Cbor.Converters.Primitives;

using System.Formats.Cbor;

public class CustomMapConverter : ICborConverter
{
    public object Deserialize(CborReader reader, CborOptions? options = null)
    {
        reader.ReadStartMap();
        Dictionary<object, object> values = [];

        // Determine if we're using string or integer keys based on options
        bool useStringKeys = options?.PropertyNameTypes?.Count > 0;
        int maxProperties = options?.Size ?? int.MaxValue;
        int propertiesRead = 0;

        while (reader.PeekState() != CborReaderState.EndMap && propertiesRead < maxProperties)
        {
            // Determine the key and its type based on the reader state
            object? key;
            Type? keyType;
            Type? valueType;

            if (useStringKeys)
            {
                key = reader.ReadTextString();

                // Find the value type based on the string key
                if (options?.PropertyNameTypes?.TryGetValue((string)key, out keyType) != true)
                {
                    reader.SkipValue(); // Skip value if key not found
                    continue;
                }

                // Get the corresponding value type
                valueType = options?.PropertyIndexTypes?.GetValueOrDefault(
                    options.PropertyNameTypes.Keys.ToList().IndexOf((string)key)
                );
            }
            else
            {
                // Handle integer keys
                if (reader.PeekState() != CborReaderState.UnsignedInteger &&
                    reader.PeekState() != CborReaderState.NegativeInteger)
                {
                    reader.SkipValue(); // Skip unexpected key
                    reader.SkipValue(); // Skip corresponding value
                    continue;
                }

                key = (int)reader.ReadInt64();

                // Find the value type based on the integer key
                if (options?.PropertyIndexTypes?.TryGetValue((int)key, out valueType) != true)
                {
                    reader.SkipValue(); // Skip value if key not found
                    continue;
                }

                keyType = typeof(int);
            }

            // If no value type found, skip the value
            if (valueType == null)
            {
                reader.SkipValue();
                continue;
            }

            // Deserialize the value
            CborOptions valueOptions = CborSerializer.GetOptions(valueType)!;
            object? value = CborSerializer.Deserialize(reader, valueOptions);

            values[key] = value!;
            propertiesRead++;
        }

        // Ensure we read the end of the map
        while (reader.PeekState() != CborReaderState.EndMap)
        {
            reader.SkipValue();
        }
        reader.ReadEndMap();

        return values;
    }

    public void Serialize(CborWriter writer, object value, CborOptions? options = null)
    {
        // Determine if we're using string or integer keys
        bool useStringKeys = options?.PropertyNameTypes?.Count > 0;

        // Get sorted properties
        object[] properties = CborSerializer.GetSortedProperties(value);

        // Write map start
        writer.WriteStartMap(options?.IsDefinite == true ? properties.Length / 2 : null);

        for (int i = 0; i < properties.Length; i += 2)
        {
            object key = properties[i];
            object propValue = properties[i + 1];

            // Write key based on whether we're using string or integer keys
            if (useStringKeys)
            {
                // Use the key as a string, preferring the actual property name if available
                string keyStr = key.ToString()!;
                writer.WriteTextString(keyStr);
            }
            else if (key is int intKey)
            {
                writer.WriteInt64(intKey);
            }
            else
            {
                throw new InvalidOperationException("Invalid key type for integer key mapping");
            }

            // Serialize the value
            CborSerializer.Serialize(writer, propValue);
        }

        writer.WriteEndMap();
    }
}