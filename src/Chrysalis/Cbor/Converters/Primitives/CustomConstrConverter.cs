using System.Collections;
using System.Formats.Cbor;
using Chrysalis.Cbor.Types;

namespace Chrysalis.Cbor.Converters.Primitives;

public class CustomConstrConverter : ICborConverter
{
    public object Deserialize(CborReader reader, CborOptions? options = null)
    {
        if (options == null)
            throw new InvalidOperationException("CborOptions must be provided.");

        // Read the start of the array that represents the constructor argument (a list)
        reader.ReadStartArray();

        // Determine the underlying element type from the ActivatorType.
        // For example, if ActivatorType is List<T> (or a record with a single parameter of type List<T>),
        // then we assume T is the underlying type.
        if (options.ActivatorType == null || !options.ActivatorType.IsGenericType)
            throw new InvalidOperationException("ActivatorType must be a generic type.");
        Type underlyingType = options.ActivatorType.GetGenericArguments()[0];

        // Get the CborOptions for the underlying element type.
        CborOptions underlyingOptions = CborSerializer.GetOptions(underlyingType)
            ?? throw new InvalidOperationException($"No options found for underlying type {underlyingType.FullName}");

        List<object?> items = [];
        // Deserialize each element in the array using the underlying options.
        while (reader.PeekState() != CborReaderState.EndArray)
        {
            object? item = CborSerializer.Deserialize(reader, underlyingOptions);
            items.Add(item);
        }
        reader.ReadEndArray();

        // Return the list of deserialized items.
        // The higher-level activator (registered for the record type) should expect this list as its constructor argument.
        return items;
    }

    public void Serialize(CborWriter writer, object value, CborOptions? options = null)
    {
        if (value is not IList list)
            throw new InvalidOperationException("Expected a list for serialization.");

        writer.WriteStartArray(options?.IsDefinite == true ? list.Count : null);
        foreach (object? item in list)
        {
            CborSerializer.Serialize(writer, item);
        }
        writer.WriteEndArray();
    }
}