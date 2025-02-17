using System.Collections;
using System.Formats.Cbor;
using System.Reflection;
using Chrysalis.Cbor.Types;

namespace Chrysalis.Cbor.Converters.Primitives;

public class ListConverter : ICborConverter
{
    public object Deserialize(CborReader reader, CborOptions? options = null)
    {
        reader.ReadStartArray();
        List<object?> list = [];

        Type underlyingType;
        if (options?.ActivatorType?.IsGenericType == true)
        {
            // Generic case (existing logic)
            underlyingType = options.ActivatorType.GetGenericArguments()[0];
        }
        else
        {
            // Non-generic case - get constructor parameter type
            ConstructorInfo[] constructorInfo = options!.ActivatorType!.GetConstructors();
            var listParam = constructorInfo[0].GetParameters()[0];
            underlyingType = listParam.ParameterType.GetGenericArguments()[0];
        }

        CborOptions underlyingOptions = CborSerializer.GetOptions(underlyingType) ??
            throw new InvalidOperationException("Underlying options not found");

        while (reader.PeekState() != CborReaderState.EndArray)
        {
            object? item = CborSerializer.Deserialize(reader, underlyingOptions);
            list.Add(item);
        }
        reader.ReadEndArray();

        // For non-generic case, wrap in the specific type
        // if (!options!.ActivatorType!.IsGenericType)
        // {
        //     return Activator.CreateInstance(options.ActivatorType, new[] { list })!;
        // }

        return list;
    }

    public void Serialize(CborWriter writer, object value, CborOptions? options = null)
    {
        ICollection collection;
        if (!options!.ActivatorType!.IsGenericType)
        {
            // Non-generic case - get constructor parameter name and find matching property
            var constructorInfo = options.ActivatorType.GetConstructors();
            var listParam = constructorInfo[0].GetParameters()[0];
            var listProperty = value.GetType().GetProperty(listParam.Name!, BindingFlags.Public | BindingFlags.Instance);
            collection = (ICollection)listProperty!.GetValue(value)!;
        }
        else
        {
            // Generic case (existing logic)
            collection = (ICollection)value;
        }

        writer.WriteStartArray(options?.IsDefinite == true ? collection.Count : null);
        foreach (object? item in collection)
        {
            CborSerializer.Serialize(writer, item);
        }
        writer.WriteEndArray();
    }
}