using System.Collections;
using System.Formats.Cbor;
using System.Reflection;
using Chrysalis.Attributes;
using Chrysalis.Types;

namespace Chrysalis.Converters;

public class ListConverter : ICborConverter
{
    public T Deserialize<T>(byte[] data) where T : Cbor
    {
        CborReader reader = new(data);
        reader.ReadStartArray();

        Type targetType = typeof(T);
        ConstructorInfo constructor = targetType.GetConstructors().FirstOrDefault() ?? throw new InvalidOperationException($"Type {targetType.Name} must have a constructor.");

        // Determine the type of the collection
        Type collectionType = constructor.GetParameters()[0].ParameterType;
        Type? elementType = collectionType.GetElementType() ?? collectionType.GetGenericArguments().FirstOrDefault();
        if (elementType == null || !typeof(Cbor).IsAssignableFrom(elementType))
        {
            throw new InvalidOperationException("The collection's element type must inherit from Cbor.");
        }

        // Create a list for the elements
        IList list = (IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(elementType))!;

        while (reader.PeekState() != CborReaderState.EndArray)
        {
            byte[] encodedValue = reader.ReadEncodedValue().ToArray();

            MethodInfo deserializeMethod = typeof(CborSerializer).GetMethod(nameof(CborSerializer.Deserialize))!;
            object item = deserializeMethod.MakeGenericMethod(elementType).Invoke(null, [encodedValue])!;

            list.Add(item);
        }

        reader.ReadEndArray();

        // Create the Cbor instance
        return (T)constructor.Invoke([list]);
    }

    public byte[] Serialize(Cbor data)
    {
        Type type = data.GetType();
        bool isDefinite = type.GetCustomAttribute<CborDefiniteAttribute>() != null;

        // Extract the collection property
        PropertyInfo collectionProperty = type.GetProperties().FirstOrDefault(p => typeof(ICollection).IsAssignableFrom(p.PropertyType))
            ?? throw new InvalidOperationException("No ICollection property found for the CborList.");

        // Get the collection
        if (collectionProperty.GetValue(data) is not ICollection collection) throw new InvalidOperationException("The collection property is null.");

        CborWriter writer = new();

        writer.WriteStartArray(isDefinite ? collection.Count : null);

        foreach (object? item in collection)
        {
            if (item is not Cbor cborItem) throw new InvalidOperationException("Collection elements must be of type Cbor.");

            // Use CborSerializer.Serialize for each item
            byte[] serialized = CborSerializer.Serialize(cborItem);
            writer.WriteEncodedValue(serialized);
        }

        writer.WriteEndArray();
        return writer.Encode();
    }
}