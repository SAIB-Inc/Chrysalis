using System.Formats.Cbor;
using Chrysalis.Cbor.Serialization;
using Chrysalis.Cbor.Serialization.Exceptions;
using Chrysalis.Cbor.Serialization.Registry;

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
            if (!options.IndexPropertyMapping.TryGetValue(i, out Type? parameterType))
                throw new CborDeserializationException($"No type found for index {i}");

            // Resolve the concrete type if it's a generic parameter
            Type innerType = parameterType;
            if (parameterType.IsGenericParameter && options.RuntimeType?.IsGenericType == true)
            {
                // Get the position of the generic parameter
                int position = parameterType.GenericParameterPosition;

                // Get the type arguments of the runtime type
                Type[] typeArgs = options.RuntimeType.GetGenericArguments();

                // If we have a type argument at this position, use it
                if (position < typeArgs.Length)
                {
                    innerType = typeArgs[position];
                }
            }

            CborOptions innerOptions = CborRegistry.Instance.GetOptions(innerType);
            object? item = CborSerializer.Deserialize(reader, innerOptions);
            items.Add(item);
        }

        reader.ReadEndArray();

        return items;
    }

    public static void Write(CborWriter writer, List<object?> propertyValues)
    {
        int count = propertyValues.Count(v => v is not null);
        if (count > 0)
        {
            for (int i = 0; i < count; i++)
            {
                object? propertyValue = propertyValues[i];
                if (propertyValue is not null)
                {
                    Type propertyType = propertyValue.GetType();
                    CborOptions innerOptions = CborRegistry.Instance.GetOptions(propertyType);
                    CborSerializer.Serialize(writer, propertyValue, innerOptions);
                }
            }
        }
    }
}