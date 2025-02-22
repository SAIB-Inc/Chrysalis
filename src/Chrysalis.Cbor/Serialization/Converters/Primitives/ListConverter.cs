using System.Formats.Cbor;
using System.Reflection;
using Chrysalis.Cbor.Serialization.Exceptions;
using Chrysalis.Cbor.Serialization.Registry;
using Chrysalis.Cbor.Utils;

namespace Chrysalis.Cbor.Serialization.Converters.Primitives;

public sealed class ListConverter : ICborConverter
{
    public object? Read(CborReader reader, CborOptions options)
    {
        if (options.RuntimeType is null)
            throw new CborDeserializationException("Runtime type not specified");

        Type innerType;
        if (options.RuntimeType.IsGenericType)
        {
            innerType = options.RuntimeType.GetGenericArguments()[0];
        }
        else
        {
            ConstructorInfo[] constructorInfos = options.RuntimeType.GetConstructors();
            ParameterInfo parameters = constructorInfos[0].GetParameters()[0];
            innerType = parameters.ParameterType.GetGenericArguments()[0];
        }

        CborOptions innerOptions = CborRegistry.Instance.GetBaseOptions(innerType);

        reader.ReadStartArray();

        List<object?> items = [];
        while (reader.PeekState() != CborReaderState.EndArray)
        {
            object? item = CborSerializer.Deserialize(reader, innerOptions);
            items.Add(item);
        }

        reader.ReadEndArray();

        return items;
    }

    public void Write(CborWriter writer, List<object?> value, CborOptions options)
    {
        if (value.First() is not IEnumerable<object> validValues)
            throw new CborSerializationException("Expected List<object?>");

        // Get inner type using helper method
        Type innerType = PropertyResolver.GetInnerType(options, value.First());
        CborOptions innerOptions = CborRegistry.Instance.GetBaseOptions(innerType);

        int count = validValues.Count(p => p is not null);
        writer.WriteStartArray(options.IsDefinite ? count : null);

        foreach (object? item in validValues)
            CborSerializer.Serialize(writer, item, innerOptions);

        writer.WriteEndArray();
    }
}