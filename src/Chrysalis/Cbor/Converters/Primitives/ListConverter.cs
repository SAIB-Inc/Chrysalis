using System.Collections;
using System.Formats.Cbor;
using System.Reflection;
using Chrysalis.Cbor.Attributes;
using Chrysalis.Cbor.Types;
using Chrysalis.Cbor.Utils;

namespace Chrysalis.Cbor.Converters.Primitives;

public class ListConverter : ICborConverter
{
    public T Deserialize<T>(byte[] data) where T : CborBase
    {
        CborReader reader = new(data);
        CborTagUtils.ReadAndVerifyTag<T>(reader);

        reader.ReadStartArray();

        Type targetType = typeof(T);
        ConstructorInfo constructor = targetType.GetConstructors().FirstOrDefault() ?? throw new InvalidOperationException($"Type {targetType.Name} must have a constructor.");

        // Determine the type of the collection
        Type collectionType = constructor.GetParameters()[0].ParameterType;
        Type? elementType = collectionType.GetElementType() ?? collectionType.GetGenericArguments().FirstOrDefault();
        if (elementType == null || !typeof(CborBase).IsAssignableFrom(elementType))
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
        T instance = (T)constructor.Invoke([list]);
        instance.Raw = data;
        return instance;
    }

    public byte[] Serialize(CborBase data)
    {
        Type type = data.GetType();
        bool isDefinite = type.GetCustomAttribute<CborDefiniteAttribute>() != null;

        // Extract the collection property
        PropertyInfo collectionProperty = type.GetProperties().FirstOrDefault(p => typeof(ICollection).IsAssignableFrom(p.PropertyType))
            ?? throw new InvalidOperationException("No ICollection property found for the CborList.");

        // Get the collection
        if (collectionProperty.GetValue(data) is not ICollection collection) throw new InvalidOperationException("The collection property is null.");

        CborWriter writer = new();
        CborTagUtils.WriteTagIfPresent(writer, type);

        writer.WriteStartArray(isDefinite ? collection.Count : null);

        foreach (object? item in collection)
        {
            if (item is not CborBase cborItem) throw new InvalidOperationException("Collection elements must be of type Cbor.");

            // Use CborSerializer.Serialize for each item
            byte[] serialized = CborSerializer.Serialize(cborItem);
            writer.WriteEncodedValue(serialized);
        }

        writer.WriteEndArray();
        return writer.Encode();
    }
}