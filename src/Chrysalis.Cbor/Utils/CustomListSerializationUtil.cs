using System.Formats.Cbor;
using Chrysalis.Cbor.Serialization;
using Chrysalis.Cbor.Serialization.Exceptions;
using Chrysalis.Cbor.Serialization.Registry;
using Chrysalis.Cbor.Types;

namespace Chrysalis.Cbor.Utils;

public static class CustomListSerializationUtil
{
    public static object? Read(CborReader reader, CborOptions options)
    {
        // A custom list is a special case of list where the constructor may have
        // parameters with different types. Each parameter must have a CborIndex attribute
        // to indicate the index of the parameter in the list.
        if (options.IndexPropertyMapping is null || options.IndexPropertyMapping.Count == 0)
            throw new CborDeserializationException("Index property mapping is not defined in options.");

        reader.ReadStartArray();

        List<object?> items = [];
        for (int i = 0; i < options.IndexPropertyMapping.Count && reader.PeekState() != CborReaderState.EndArray; i++)
        {
            if (!options.IndexPropertyMapping.TryGetValue(i, out (Type Type, object? ExpectedValue) parameterType))
                throw new CborDeserializationException($"No type found for index {i}");

            // Resolve the concrete type if it's a generic parameter
            Type innerType = parameterType.Type;
            if (innerType.IsGenericParameter && options.RuntimeType?.IsGenericType == true)
            {
                // Get the position of the generic parameter
                int position = innerType.GenericParameterPosition;

                // Get the type arguments of the runtime type
                Type[] typeArgs = options.RuntimeType.GetGenericArguments();

                // If we have a type argument at this position, use it
                if (position < typeArgs.Length)
                {
                    innerType = typeArgs[position];
                }
            }

            object? item = innerType.TryCallStaticRead(reader);
            items.Add(item);
        }

        reader.ReadEndArray();

        return items;
    }

    public static void Write(CborWriter writer, List<object?> propertyValues)
    {
        // Filter out null values to serialize only non-null elements
        List<object?> nonNullValues = propertyValues.Where(v => v != null).ToList();
        int count = nonNullValues.Count;

        // Write each non-null property value
        for (int i = 0; i < count; i++)
        {
            object propertyValue = nonNullValues[i]!;
            CborBase? propertyValueCbor = propertyValue as CborBase;
            List<object?> filteredProperties = PropertyResolver.GetFilteredProperties(propertyValue);
            propertyValueCbor!.Write(writer, filteredProperties);
        }
    }
}