using System.Formats.Cbor;
using Chrysalis.Cbor.Types;

namespace Chrysalis.Cbor.Converters.Primitives;

public class ConstrConverter : ICborConverter
{
    public object Deserialize(CborReader reader, CborOptions? options = null)
    {
        // The target type is obtained from options.ActivatorType.
        Type targetType = options?.ActivatorType
            ?? throw new InvalidOperationException("Activator type not specified in options");

        // Read the array that represents constructor arguments.
        reader.ReadStartArray();

        // Retrieve the expected property types (ordered by index).
        Dictionary<int, Type> propertyTypes = options?.PropertyIndexTypes
            ?? throw new InvalidOperationException("Property types not specified in options");

        int maxProperties = options?.Size ?? propertyTypes.Count;
        List<object?> constructorArgs = new List<object?>(maxProperties);

        // For each expected constructor argument (by index), deserialize using the registry.
        for (int i = 0; i < maxProperties && reader.PeekState() != CborReaderState.EndArray; i++)
        {
            if (!propertyTypes.TryGetValue(i, out Type? propType))
            {
                throw new InvalidOperationException($"No type found for index {i}");
            }
            // Get the options for the property type from the registry.
            CborOptions? propOptions = CborSerializer.GetOptions(propType);
            // Deserialize the next array element using the property options.
            object? arg = CborSerializer.Deserialize(reader, propOptions);
            constructorArgs.Add(arg);
        }

        // Skip any extra elements.
        while (reader.PeekState() != CborReaderState.EndArray)
        {
            reader.SkipValue();
        }
        reader.ReadEndArray();

        return constructorArgs;
        // // Create the instance using the resolved constructor arguments.
        // object instance = Activator.CreateInstance(targetType, constructorArgs.ToArray())
        //     ?? throw new InvalidOperationException($"Could not create instance of {targetType.FullName}");
        // return instance;
    }

    public void Serialize(CborWriter writer, object value, CborOptions? options = null)
    {
        // Use the registry helper (or existing CborSerializer method) to get sorted property values.
        object[] propertyValues = CborSerializer.GetSortedProperties(value);
        writer.WriteStartArray(options?.IsDefinite == true ? propertyValues.Length : null);
        foreach (object propValue in propertyValues)
        {
            CborSerializer.Serialize(writer, propValue);
        }
        writer.WriteEndArray();
    }
}