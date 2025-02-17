using System.Formats.Cbor;
using System.Reflection;
using Chrysalis.Cbor.Serialization.Registry;

namespace Chrysalis.Cbor.Serialization.Converters.Primitives;

public sealed class ListConverter : ICborConverter
{
    public object? Read(CborReader reader, CborOptions options)
    {
        if (options.RuntimeType is null)
            throw new InvalidOperationException("Runtime type not specified");

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

        CborOptions innerOptions = CborRegistry.Instance.GetOptions(innerType);

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

    public void Write(CborWriter writer, object? value, CborOptions options)
    {
        throw new NotImplementedException();
    }
}